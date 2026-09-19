using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// An organization keeps at least one owner, and keeps one when two owners leave at the same
/// instant.
/// </summary>
/// <remarks>
/// The aggregate counts the owners it loaded, and two requests that each loaded two and each
/// remove one both pass that count. Only the database can see both, so only a real Postgres
/// can show it refusing the second: the token is xmin, Postgres's own.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class OwnerRaceTests(SpaceCraftApi api)
{
    [Fact]
    public async Task Two_owners_leaving_at_once_cannot_leave_none()
    {
        var (organizationId, first, second) = await SeedTwoOwnersAsync();

        // Both requests have read the organization with its two owners before either writes.
        await using var firstScope = api.Services.CreateAsyncScope();
        await using var secondScope = api.Services.CreateAsyncScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var seenByFirst = await LoadAsync(firstDb, organizationId);
        var seenBySecond = await LoadAsync(secondDb, organizationId);

        seenByFirst.RemoveMember(first);
        await firstDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Passes the aggregate — it still sees two owners — and is stopped by the row.
        seenBySecond.RemoveMember(second);

        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => secondDb.SaveChangesAsync(TestContext.Current.CancellationToken));

        await using var verify = api.Services.CreateAsyncScope();
        var owners = await verify.ServiceProvider.GetRequiredService<SpaceCraftDbContext>().OrganizationMembers
            .Where(m => m.OrganizationId == organizationId && m.Role == OrganizationRole.Owner)
            .Select(m => m.UserId)
            .ToListAsync(TestContext.Current.CancellationToken);

        owners.ShouldBe([second]);
    }

    [Fact]
    public async Task Deleting_an_account_leaves_its_organizations_the_way_a_member_does()
    {
        var founderEmail = Email();
        var founder = await api.SignedInAsync(founderEmail);
        var organizationId = await OrganizationAsync(founder);

        var leaverEmail = Email();
        var leaver = await api.SignedInAsync(leaverEmail);
        await JoinAsync(organizationId, leaverEmail, OrganizationRole.Owner);

        // Two owners, so the second may go.
        var deleted = await DeleteAccountAsync(leaver);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // And now the founder is the only one, and may not — stopped by the check that names
        // the organizations, which runs before anything is loaded; the aggregate's own refusal
        // is for the owner who was not the only one a query earlier, below.
        var refused = await DeleteAccountAsync(founder);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(refused)).Detail.ShouldStartWith("Hand over or delete these organizations first:");

        var members = await MembersAsync(organizationId);
        members.ShouldHaveSingleItem().Role.ShouldBe(OrganizationRole.Owner);
    }

    [Fact]
    public async Task Deleting_an_account_as_the_other_owner_leaves_is_refused_and_keeps_the_session()
    {
        var founderEmail = Email();
        var founder = await api.SignedInAsync(founderEmail);
        var organizationId = await OrganizationAsync(founder);
        var founderId = await UserIdAsync(founderEmail);

        var otherEmail = Email();
        await api.SignedInAsync(otherEmail);
        var otherId = await JoinAsync(organizationId, otherEmail, OrganizationRole.Owner);

        // Two owners, so the blockers let the founder through, and so does the aggregate, on
        // the two it loaded. The other owner leaves inside the founder's save — after the
        // request read the organization and before it wrote — which is the interleaving two
        // requests sent at once only sometimes produce.
        api.BeforeSaving.Arm(
            when: db => db.ChangeTracker.Entries<OrganizationMember>()
                .Any(e => e.State == EntityState.Deleted && e.Entity.UserId == founderId),
            run: async ct =>
            {
                await using var scope = api.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
                var organization = await LoadAsync(db, organizationId);

                organization.RemoveMember(otherId);
                await db.SaveChangesAsync(ct);
            });

        var refused = await DeleteAccountAsync(founder);

        // The row refused it — the founder's write named an xmin the other owner's had
        // replaced — and the answer is the token's, not the blockers' nor the aggregate's.
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(refused)).Detail.ShouldBe("Someone else changed this at the same time. Reload and try again.");

        // Refused means nothing was written: the founder is the organization's owner, and is
        // still signed in to reload and try again — the sessions the request had lined up to
        // revoke went back with the rest of it.
        var members = await MembersAsync(organizationId);
        members.ShouldHaveSingleItem().UserId.ShouldBe(founderId);
        members[0].Role.ShouldBe(OrganizationRole.Owner);

        var account = await founder.GetAsync(new Uri("/api/v1/account", UriKind.Relative), TestContext.Current.CancellationToken);
        account.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<ProblemDetails> ProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        problem.ShouldNotBeNull();
        return problem;
    }

    private async Task<List<OrganizationMember>> MembersAsync(Guid organizationId)
    {
        await using var scope = api.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>().OrganizationMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> DeleteAccountAsync(HttpClient client) =>
        client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, new Uri("/api/v1/account", UriKind.Relative))
            {
                Content = JsonContent.Create(new { password = "Correct-Horse-Battery-9!" }),
            },
            TestContext.Current.CancellationToken);

    private static Task<Organization> LoadAsync(SpaceCraftDbContext db, Guid organizationId) =>
        db.Organizations
            .Include(o => o.Members)
            .SingleAsync(o => o.Id == organizationId, TestContext.Current.CancellationToken);

    private async Task<(Guid OrganizationId, Guid First, Guid Second)> SeedTwoOwnersAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var slug = $"owners-{Guid.CreateVersion7():N}"[..24];
        var organization = new Organization("Owners", slug, first);
        organization.AddMember(second, OrganizationRole.Owner);

        db.Organizations.Add(organization);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (organization.Id, first, second);
    }

    private static string Email() => $"{Guid.NewGuid():N}@example.test";

    private static async Task<Guid> OrganizationAsync(HttpClient client)
    {
        var created = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new { name = $"Org{Guid.NewGuid():N}"[..14] },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();

        return (await created.Content.ReadFromJsonAsync<IdPayload>(TestContext.Current.CancellationToken))!.Id;
    }

    private async Task<Guid> JoinAsync(Guid organizationId, string email, OrganizationRole role)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var userId = await UserIdAsync(email);
        var organization = await LoadAsync(db, organizationId);
        organization.AddMember(userId, role);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return userId;
    }

    private async Task<Guid> UserIdAsync(string email)
    {
        await using var scope = api.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>().Users
            .Where(u => u.Email == email)
            .Select(u => u.Id)
            .FirstAsync(TestContext.Current.CancellationToken);
    }

    private sealed record IdPayload(Guid Id);
}
