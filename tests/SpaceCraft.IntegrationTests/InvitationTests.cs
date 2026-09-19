using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// Joining an organization, and the address squatting it used to allow.
/// </summary>
/// <remarks>
/// Membership was granted by resolving an address to whatever account already used it, and
/// nothing in this system has ever proved an address belongs to its account —
/// <c>EmailConfirmed</c> is written by a migration and read by nobody. Registering a
/// colleague's work address was therefore enough to be handed their organization the day an
/// admin invited them. The proof now lives in the mailbox: a token is sent there, and only
/// someone who can read it can accept.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class InvitationTests(SpaceCraftApi api)
{
    [Fact]
    public async Task Inviting_an_address_grants_nothing_until_the_link_is_opened()
    {
        var owner = await api.SignedInAsync(Email());
        var organizationId = await OrganizationAsync(owner);

        var inviteeEmail = Email();
        var invitee = await api.SignedInAsync(inviteeEmail);

        var invited = await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/members", UriKind.Relative),
            new { email = inviteeEmail, role = (int)OrganizationRole.Member },
            TestContext.Current.CancellationToken);

        // Accepted, not created: nobody has joined anything yet.
        invited.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
            (await db.OrganizationMembers.CountAsync(
                m => m.OrganizationId == organizationId, TestContext.Current.CancellationToken))
                .ShouldBe(1, "only the owner is a member until the invitation is accepted");
        }

        // What the recipient actually receives.
        var token = TokenFrom(await api.Mailbox.WaitForAsync(inviteeEmail, "invited", TestContext.Current.CancellationToken));

        var accepted = await invitee.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
            (await db.OrganizationMembers.CountAsync(
                m => m.OrganizationId == organizationId, TestContext.Current.CancellationToken))
                .ShouldBe(2);
        }
    }

    [Fact]
    public async Task Registering_someone_elses_address_does_not_hand_over_their_organization()
    {
        // The attack the flow exists to stop. Mallory registers the address she expects an
        // admin to invite, and waits.
        var owner = await api.SignedInAsync(Email());
        var organizationId = await OrganizationAsync(owner);

        var squattedAddress = Email();
        var mallory = await api.SignedInAsync(squattedAddress);

        await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/members", UriKind.Relative),
            new { email = squattedAddress, role = (int)OrganizationRole.Admin },
            TestContext.Current.CancellationToken);

        // She is not a member, and cannot become one without the token — which went to the
        // mailbox, not to her account.
        var probe = await mallory.GetAsync(
            new Uri($"/api/v1/organizations/{organizationId}/credentials", UriKind.Relative),
            TestContext.Current.CancellationToken);

        probe.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And guessing is not a route either.
        var guessed = await mallory.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token = new string('a', 64) },
            TestContext.Current.CancellationToken);

        guessed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_second_invitation_puts_the_first_one_out()
    {
        // An older link in an older email stops working, the rule password resets follow.
        var owner = await api.SignedInAsync(Email());
        var organizationId = await OrganizationAsync(owner);

        var inviteeEmail = Email();
        var invitee = await api.SignedInAsync(inviteeEmail);

        await Invite(owner, organizationId, inviteeEmail, OrganizationRole.Viewer);
        var first = TokenFrom(await api.Mailbox.WaitForAsync(inviteeEmail, "invited", TestContext.Current.CancellationToken));

        await Invite(owner, organizationId, inviteeEmail, OrganizationRole.Member);

        // Waits for the second invitation, so the first is known to have been superseded.
        await api.Mailbox.WaitForAsync(inviteeEmail, "invited", TestContext.Current.CancellationToken, ordinal: 2);

        var stale = await invitee.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token = first },
            TestContext.Current.CancellationToken);

        stale.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_invitation_works_once()
    {
        var owner = await api.SignedInAsync(Email());
        var organizationId = await OrganizationAsync(owner);

        var inviteeEmail = Email();
        var invitee = await api.SignedInAsync(inviteeEmail);

        await Invite(owner, organizationId, inviteeEmail, OrganizationRole.Member);
        var token = TokenFrom(await api.Mailbox.WaitForAsync(inviteeEmail, "invited", TestContext.Current.CancellationToken));

        (await Accept(invitee, token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Accept(invitee, token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static Task<HttpResponseMessage> Invite(
        HttpClient client, Guid organizationId, string email, OrganizationRole role) =>
        client.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/members", UriKind.Relative),
            new { email, role = (int)role },
            TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> Accept(HttpClient client, string token) =>
        client.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);

    /// <summary>Reads the link out of the message, the way a recipient clicks it.</summary>
    [Fact]
    public async Task An_invitation_can_be_read_without_being_spent()
    {
        var owner = await api.SignedInAsync(Email());
        var organizationId = await OrganizationAsync(owner);

        await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/projects", UriKind.Relative),
            new { name = "Ledger API" },
            TestContext.Current.CancellationToken);

        var inviteeEmail = Email();
        var invitee = await api.SignedInAsync(inviteeEmail);

        await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/members", UriKind.Relative),
            new { email = inviteeEmail, role = (int)OrganizationRole.Member },
            TestContext.Current.CancellationToken);

        var token = TokenFrom(await api.Mailbox.WaitForAsync(inviteeEmail, "invited", TestContext.Current.CancellationToken));

        var previewed = await invitee.PostAsJsonAsync(
            "/api/v1/invitations/preview",
            new { token },
            TestContext.Current.CancellationToken);

        previewed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var preview = await previewed.Content.ReadFromJsonAsync<PreviewPayload>(
            TestContext.Current.CancellationToken);

        preview!.Role.ShouldBe((int)OrganizationRole.Member);
        preview.MemberCount.ShouldBe(1);
        preview.Projects.ShouldContain("Ledger API");
        preview.AlreadyMember.ShouldBeFalse();

        // Read, not spent: the same token still joins afterwards.
        var accepted = await invitee.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Declining_puts_the_link_out()
    {
        var owner = await api.SignedInAsync(Email());
        var organizationId = await OrganizationAsync(owner);

        var inviteeEmail = Email();
        var invitee = await api.SignedInAsync(inviteeEmail);

        await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/members", UriKind.Relative),
            new { email = inviteeEmail, role = (int)OrganizationRole.Member },
            TestContext.Current.CancellationToken);

        var token = TokenFrom(await api.Mailbox.WaitForAsync(inviteeEmail, "invited", TestContext.Current.CancellationToken));

        var declined = await invitee.PostAsJsonAsync(
            "/api/v1/invitations/decline",
            new { token },
            TestContext.Current.CancellationToken);

        declined.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Out for good, and indistinguishable from every other unusable token.
        var accepted = await invitee.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token },
            TestContext.Current.CancellationToken);

        accepted.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Gone from the list an admin reads, too. It once stayed there — the list carried its
        // own copy of "still usable" and nobody told it about declining — so an invitation
        // already refused went on being offered, with a link that no longer worked.
        var pending = await owner.GetAsync(
            new Uri($"/api/v1/organizations/{organizationId}/members/invitations", UriKind.Relative),
            TestContext.Current.CancellationToken);

        pending.EnsureSuccessStatusCode();
        (await pending.Content.ReadFromJsonAsync<List<InvitationPayload>>(
            TestContext.Current.CancellationToken))!.ShouldBeEmpty();

        // And it is recorded as a refusal, not as a withdrawal: only one of the two is an
        // answer, and conflating them would make that unanswerable later.
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var invitation = await db.OrganizationInvitations
            .FirstAsync(i => i.OrganizationId == organizationId, TestContext.Current.CancellationToken);

        invitation.DeclinedAt.ShouldNotBeNull();
        invitation.RevokedAt.ShouldBeNull();
    }

    private static string TokenFrom(SpaceCraft.Api.Email.EmailMessage message)
    {
        var match = Regex.Match(message.TextBody, @"token=([A-Za-z0-9%]+)");
        match.Success.ShouldBeTrue("the invitation email carries an accept link");

        return Uri.UnescapeDataString(match.Groups[1].Value);
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

    private sealed record IdPayload(Guid Id);

    private sealed record InvitationPayload(Guid Id, string Email);

    private sealed record PreviewPayload(
        string OrganizationName, string OrganizationSlug, int Role, string InvitedBy,
        int MemberCount, List<string> Projects, bool AlreadyMember);
}
