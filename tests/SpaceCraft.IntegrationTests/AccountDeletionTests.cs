using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpaceCraft.Domain.Identity;
using SpaceCraft.Domain.Notifications;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// Deleting an account takes with it every row that is only that account's.
/// </summary>
/// <remarks>
/// None of those rows has a foreign key to the user, so nothing in the schema would notice
/// one left behind; only counting them after the fact does. Against the real database,
/// because the delete is one transaction across our tables and Identity's, and a test
/// double would not have one.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class AccountDeletionTests(SpaceCraftApi api)
{
    [Fact]
    public async Task Everything_the_account_owns_goes_with_it()
    {
        var email = Email();
        var client = await api.SignedInAsync(email);
        var seeded = await SeedAsync(email);

        var deleted = await client.SendAsync(DeleteAccount(), Ct);

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ShouldBeGoneAsync(seeded);
    }

    [Fact]
    public async Task A_transient_failure_replays_the_whole_delete()
    {
        var email = Email();
        var client = await api.SignedInAsync(email);
        var seeded = await SeedAsync(email);

        // The database refuses the user row once, the way a serialization failure or a lost
        // connection would, at the last step: everything before it has been written and is
        // rolled back with it. The retrying strategy then runs the unit again on the same
        // context, and a replay that carried the first attempt's tracked rows answered 409
        // ("not a member", or a conflict on the organization's token) instead of deleting.
        await FailTheFirstDeleteAsync();
        try
        {
            var deleted = await client.SendAsync(DeleteAccount(), Ct);

            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await AttemptsAsync()).ShouldBe(2);
        }
        finally
        {
            await AllowDeletionAsync();
        }

        await ShouldBeGoneAsync(seeded);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Email() => $"{Guid.NewGuid():N}@example.test";

    private static HttpRequestMessage DeleteAccount() =>
        new(HttpMethod.Delete, new Uri("/api/v1/account", UriKind.Relative))
        {
            Content = JsonContent.Create(new { password = "Correct-Horse-Battery-9!" }),
        };

    private async Task ShouldBeGoneAsync(Seeded seeded)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        (await db.Users.AnyAsync(u => u.Id == seeded.UserId, Ct)).ShouldBeFalse();

        // The membership left through its organization, which stays with its owner.
        (await db.OrganizationMembers.AnyAsync(m => m.UserId == seeded.UserId, Ct)).ShouldBeFalse();
        (await db.Organizations.AnyAsync(o => o.Id == seeded.OrganizationId, Ct)).ShouldBeTrue();

        // Signing in made the session; the rest was seeded. Each table names the user by id
        // alone, and each is empty of it now.
        (await db.RefreshTokens.AnyAsync(t => t.UserId == seeded.UserId, Ct)).ShouldBeFalse();
        (await db.PasswordResetTokens.AnyAsync(t => t.UserId == seeded.UserId, Ct)).ShouldBeFalse();
        (await db.NotificationPreferences.AnyAsync(p => p.UserId == seeded.UserId, Ct)).ShouldBeFalse();
        (await db.Notifications.AnyAsync(n => n.UserId == seeded.UserId, Ct)).ShouldBeFalse();

        // The delivery hangs off the notification by the one cascade the database holds, and
        // the notification went by ExecuteDelete — so it is the database's cascade, not EF's,
        // that has to have removed it.
        (await db.NotificationDeliveries.AnyAsync(d => d.Id == seeded.DeliveryId, Ct)).ShouldBeFalse();
    }

    /// <summary>
    /// A membership in a colleague's organization, a reset link, a preference, and a
    /// notification with a delivery — one row in every table the deleter clears.
    /// </summary>
    private async Task<Seeded> SeedAsync(string email)
    {
        var colleague = await api.SignedInAsync(Email());
        var organization = await colleague.PostAsJsonAsync(
            "/api/v1/organizations", new { name = $"Org{Guid.NewGuid():N}"[..14] }, Ct);

        organization.EnsureSuccessStatusCode();
        var organizationId = (await organization.Content.ReadFromJsonAsync<IdPayload>(Ct))!.Id;

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var userId = await db.Users
            .Where(u => u.Email == email)
            .Select(u => u.Id)
            .SingleAsync(Ct);

        var joined = await db.Organizations
            .Include(o => o.Members)
            .SingleAsync(o => o.Id == organizationId, Ct);

        joined.AddMember(userId, OrganizationRole.Admin);

        db.PasswordResetTokens.Add(new PasswordResetToken(
            userId, Convert.ToHexString(Guid.NewGuid().ToByteArray()), DateTimeOffset.UtcNow.AddHours(1)));

        db.NotificationPreferences.Add(new NotificationPreference(
            userId, NotificationKind.MemberJoined, NotificationChannel.InApp, enabled: false));

        var notification = new Notification(
            userId, organizationId, NotificationKind.MemberJoined,
            "Someone joined", "A colleague accepted an invitation.", null, "/members", "See members",
            showInApp: true);
        notification.RequestDelivery(NotificationChannel.InApp);
        db.Notifications.Add(notification);

        await db.SaveChangesAsync(Ct);
        return new Seeded(userId, organizationId, notification.Deliveries.Single().Id);
    }

    /// <summary>
    /// A trigger that refuses the first delete of a user row with a serialization failure,
    /// which Npgsql reports as transient, and lets every later one through. Counted on a
    /// sequence because a sequence is the one thing the rollback does not undo. Every test in
    /// the collection runs one after another against this database, so it stops nobody else's.
    /// </summary>
    private async Task FailTheFirstDeleteAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "CREATE SEQUENCE spacecraft.account_deletion_attempts", Ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE OR REPLACE FUNCTION spacecraft.fail_first_deletion() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                IF nextval('spacecraft.account_deletion_attempts') = 1 THEN
                    RAISE EXCEPTION 'Refused once by AccountDeletionTests.' USING ERRCODE = '40001';
                END IF;
                RETURN OLD;
            END $$;
            """,
            Ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER fail_first_deletion BEFORE DELETE ON spacecraft.users
            FOR EACH ROW EXECUTE FUNCTION spacecraft.fail_first_deletion();
            """,
            Ct);
    }

    /// <summary>How many times the user row was asked to go.</summary>
    private async Task<long> AttemptsAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        return await db.Database
            .SqlQueryRaw<long>("SELECT last_value AS \"Value\" FROM spacecraft.account_deletion_attempts")
            .SingleAsync(Ct);
    }

    private async Task AllowDeletionAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER IF EXISTS fail_first_deletion ON spacecraft.users", Ct);
        await db.Database.ExecuteSqlRawAsync(
            "DROP FUNCTION IF EXISTS spacecraft.fail_first_deletion", Ct);
        await db.Database.ExecuteSqlRawAsync(
            "DROP SEQUENCE IF EXISTS spacecraft.account_deletion_attempts", Ct);
    }

    private sealed record Seeded(Guid UserId, Guid OrganizationId, Guid DeliveryId);

    private sealed record IdPayload(Guid Id);
}
