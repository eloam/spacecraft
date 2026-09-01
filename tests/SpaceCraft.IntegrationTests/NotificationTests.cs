using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using SpaceCraft.Domain.Organizations;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// Who gets told what, and who does not.
/// </summary>
/// <remarks>
/// Every assertion here is made through the API a person uses — the feed, the settings, the
/// mailbox — rather than against the rows behind it. A notification nobody can read is not a
/// notification, and the table being right is not the property worth pinning.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class NotificationTests(SpaceCraftApi api)
{
    [Fact]
    public async Task Somebody_joining_tells_the_owner_and_not_the_joiner()
    {
        var ownerEmail = Email();
        var owner = await api.SignedInAsync(ownerEmail);
        var (organizationId, slug) = await OrganizationWithSlugAsync(owner);

        var joinerEmail = Email();
        var joiner = await api.SignedInAsync(joinerEmail);

        await JoinByInvitationAsync(owner, joiner, organizationId, joinerEmail);

        var forOwner = await FeedAsync(owner);
        forOwner.UnreadCount.ShouldBe(1);
        forOwner.Items[0].Kind.ShouldBe("MemberJoined");
        forOwner.Items[0].Title.ShouldContain(NameOf(joinerEmail));

        // The display name answers "who" only for somebody who already knows them. The
        // address is the account, and the account is what an admin acts on.
        forOwner.Items[0].Summary.ShouldContain(joinerEmail);
        // The slug, not the id it is stored with: a notification is a record, but a link is
        // an address, and every other address in the application reads as a name.
        forOwner.Items[0].Link.ShouldBe($"/orgs/{slug}/members");

        // The joiner is looking at the result of their own click.
        (await FeedAsync(joiner)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Leaving_tells_the_owner_and_not_the_person_who_left()
    {
        var owner = await api.SignedInAsync(Email());
        var organizationId = await OrganizationAsync(owner);

        var leaverEmail = Email();
        var leaver = await api.SignedInAsync(leaverEmail);
        var leaverId = await JoinByInvitationAsync(owner, leaver, organizationId, leaverEmail);

        var left = await leaver.DeleteAsync(
            new Uri($"/api/v1/organizations/{organizationId}/members/{leaverId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        left.EnsureSuccessStatusCode();

        var forOwner = await FeedAsync(owner);
        forOwner.Items.ShouldContain(n =>
            n.Kind == "MemberLeft"
            && n.Title.Contains(NameOf(leaverEmail), StringComparison.Ordinal)
            && n.Summary.Contains(leaverEmail, StringComparison.Ordinal));

        // And they hear nothing about the organization they have left, including about
        // leaving it.
        (await FeedAsync(leaver)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Leaving_an_organization_takes_its_notifications_with_it()
    {
        var owner = await api.SignedInAsync(Email());
        var organizationId = await OrganizationAsync(owner);

        // An admin, so that somebody else joining is news to them too.
        var adminEmail = Email();
        var admin = await api.SignedInAsync(adminEmail);
        var adminId = await JoinByInvitationAsync(
            owner, admin, organizationId, adminEmail, OrganizationRole.Admin);

        var joinerEmail = Email();
        var joiner = await api.SignedInAsync(joinerEmail);
        await JoinByInvitationAsync(owner, joiner, organizationId, joinerEmail);

        (await FeedAsync(admin)).Items.ShouldContain(n => n.Kind == "MemberJoined");

        var removed = await owner.DeleteAsync(
            new Uri($"/api/v1/organizations/{organizationId}/members/{adminId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        removed.EnsureSuccessStatusCode();

        // Every one of those items pointed at a page they can no longer open. What survives is
        // the one notification that exists because they are no longer a member.
        var forAdmin = await FeedAsync(admin);
        forAdmin.Items.ShouldHaveSingleItem().Kind.ShouldBe("RemovedFromOrganization");
    }

    [Fact]
    public async Task Declining_tells_the_organization_it_was_refused()
    {
        var ownerEmail = Email();
        var owner = await api.SignedInAsync(ownerEmail);
        var organizationId = await OrganizationAsync(owner);

        var inviteeEmail = Email();
        var invitee = await api.SignedInAsync(inviteeEmail);

        await owner.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/members", UriKind.Relative),
            new { email = inviteeEmail, role = (int)OrganizationRole.Member },
            TestContext.Current.CancellationToken);

        var invitation = await api.Mailbox.WaitForAsync(
            inviteeEmail, "invited", TestContext.Current.CancellationToken);

        var match = Regex.Match(invitation.TextBody, @"token=([A-Za-z0-9%]+)");

        var declined = await invitee.PostAsJsonAsync(
            "/api/v1/invitations/decline",
            new { token = Uri.UnescapeDataString(match.Groups[1].Value) },
            TestContext.Current.CancellationToken);

        declined.EnsureSuccessStatusCode();

        // An answer, not silence: whoever picks the thread up next needs to know it was
        // refused rather than still travelling.
        var forOwner = await FeedAsync(owner);
        var told = forOwner.Items.ShouldHaveSingleItem();

        told.Kind.ShouldBe("InvitationDeclined");
        told.Title.ShouldContain(inviteeEmail);

        // Standing on its own: an admin who did not send this invitation should not have to
        // work out who "they" were from the line above.
        told.Summary.ShouldContain(inviteeEmail);
        told.Summary.ShouldContain("Member");
        told.Detail.ShouldNotBeNull().ShouldContain("Nobody has been added");

        // And the person who declined is not a member, so hears nothing about it.
        (await FeedAsync(invitee)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Being_removed_tells_the_person_it_happened_to()
    {
        var ownerEmail = Email();
        var owner = await api.SignedInAsync(ownerEmail);
        var organizationId = await OrganizationAsync(owner);

        var memberEmail = Email();
        var member = await api.SignedInAsync(memberEmail);
        var memberId = await JoinByInvitationAsync(owner, member, organizationId, memberEmail);

        var removed = await owner.DeleteAsync(
            new Uri($"/api/v1/organizations/{organizationId}/members/{memberId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        removed.EnsureSuccessStatusCode();

        // Read despite no longer being a member, which is the whole point: the membership
        // filter the feed applies would otherwise throw away the news of leaving.
        var theirs = await FeedAsync(member);
        var notification = theirs.Items.ShouldHaveSingleItem();

        notification.Kind.ShouldBe("RemovedFromOrganization");
        notification.Title.ShouldStartWith("You were removed from");
        notification.Link.ShouldBe("/");

        // And it reaches the mailbox, which is the only channel that still finds them.
        var message = await api.Mailbox.WaitForAsync(
            memberEmail, "removed", TestContext.Current.CancellationToken);

        message.TextBody.ShouldContain(ownerEmail);
    }

    [Fact]
    public async Task Leaving_of_your_own_accord_tells_you_nothing()
    {
        var owner = await api.SignedInAsync(Email());
        var organizationId = await OrganizationAsync(owner);

        var leaverEmail = Email();
        var leaver = await api.SignedInAsync(leaverEmail);
        var leaverId = await JoinByInvitationAsync(owner, leaver, organizationId, leaverEmail);

        var left = await leaver.DeleteAsync(
            new Uri($"/api/v1/organizations/{organizationId}/members/{leaverId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        left.EnsureSuccessStatusCode();

        // Nobody needs telling what they have just done.
        (await FeedAsync(leaver)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Turning_a_notification_off_stops_it_being_raised_at_all()
    {
        var owner = await api.SignedInAsync(Email());
        var organizationId = await OrganizationAsync(owner);

        var preferences = await PreferencesAsync(owner);

        var silenced = preferences
            .Select(p => p.Kind == "MemberJoined" ? p with { Enabled = false } : p)
            .ToList();

        var saved = await owner.PutAsJsonAsync(
            "/api/v1/notifications/preferences",
            new { preferences = silenced },
            TestContext.Current.CancellationToken);

        saved.EnsureSuccessStatusCode();

        var joinerEmail = Email();
        var joiner = await api.SignedInAsync(joinerEmail);
        await JoinByInvitationAsync(owner, joiner, organizationId, joinerEmail);

        // Not merely hidden: with no channel left to deliver on, nothing is written.
        (await FeedAsync(owner)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_setting_that_was_saved_is_the_setting_that_comes_back()
    {
        var user = await api.SignedInAsync(Email());

        var defaults = await PreferencesAsync(user);
        defaults.ShouldAllBe(p => p.Enabled, "everything is on until somebody says otherwise");
        defaults.Count.ShouldBe(10, "five kinds on two channels");

        var changed = defaults
            .Select(p => p.Channel == "Email" ? p with { Enabled = false } : p)
            .ToList();

        var response = await user.PutAsJsonAsync(
            "/api/v1/notifications/preferences",
            new { preferences = changed },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var reloaded = await PreferencesAsync(user);
        reloaded.Where(p => p.Channel == "Email").ShouldAllBe(p => !p.Enabled);
        reloaded.Where(p => p.Channel == "InApp").ShouldAllBe(p => p.Enabled);
    }

    [Fact]
    public async Task One_can_be_thrown_away_and_only_by_the_person_it_was_for()
    {
        var owner = await api.SignedInAsync(Email());
        var organizationId = await OrganizationAsync(owner);

        var joinerEmail = Email();
        var joiner = await api.SignedInAsync(joinerEmail);
        await JoinByInvitationAsync(owner, joiner, organizationId, joinerEmail);

        var notification = (await FeedAsync(owner)).Items.ShouldHaveSingleItem();
        var uri = new Uri($"/api/v1/notifications/{notification.Id}", UriKind.Relative);

        // Somebody else's list is not theirs to tidy up — and the refusal says nothing about
        // whether that id exists.
        var byAnother = await joiner.DeleteAsync(uri, TestContext.Current.CancellationToken);
        byAnother.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var deleted = await owner.DeleteAsync(uri, TestContext.Current.CancellationToken);
        deleted.EnsureSuccessStatusCode();

        var after = await FeedAsync(owner);
        after.Items.ShouldBeEmpty();
        after.UnreadCount.ShouldBe(0, "the count follows the list");
    }

    [Fact]
    public async Task Clearing_spares_what_has_not_been_read_unless_it_is_asked_to()
    {
        var owner = await api.SignedInAsync(Email());
        var organizationId = await OrganizationAsync(owner);

        foreach (var _ in Enumerable.Range(0, 2))
        {
            var joinerEmail = Email();
            var joiner = await api.SignedInAsync(joinerEmail);
            await JoinByInvitationAsync(owner, joiner, organizationId, joinerEmail);
        }

        var before = await FeedAsync(owner);
        before.Items.Count.ShouldBe(2);

        var read = await owner.PostAsync(
            new Uri($"/api/v1/notifications/{before.Items[0].Id}/read", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        read.EnsureSuccessStatusCode();

        // Tidying up is the common intention; losing something unread is the expensive
        // mistake. Saying nothing about the unread ones keeps them.
        var cleared = await owner.DeleteAsync(
            new Uri("/api/v1/notifications", UriKind.Relative), TestContext.Current.CancellationToken);

        cleared.EnsureSuccessStatusCode();

        var after = await FeedAsync(owner);
        after.Items.ShouldHaveSingleItem().ReadAt.ShouldBeNull();
        after.UnreadCount.ShouldBe(1);

        var everything = await owner.DeleteAsync(
            new Uri("/api/v1/notifications?includeUnread=true", UriKind.Relative),
            TestContext.Current.CancellationToken);

        everything.EnsureSuccessStatusCode();
        (await FeedAsync(owner)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_setting_named_twice_settles_on_the_last_word()
    {
        var user = await api.SignedInAsync(Email());

        // The page never sends the same switch twice, but a request that does used to reach
        // the unique index and come back a 500 — an error about our schema, for something
        // that was merely repetitive.
        var repeated = (await PreferencesAsync(user))
            .Append(new Preference("RunCompleted", "Email", false))
            .ToList();

        var response = await user.PutAsJsonAsync(
            "/api/v1/notifications/preferences",
            new { preferences = repeated },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var reloaded = await PreferencesAsync(user);
        reloaded.Single(p => p.Kind == "RunCompleted" && p.Channel == "Email").Enabled.ShouldBeFalse();
        reloaded.Count.ShouldBe(10, "the repetition wrote one row, not two");
    }

    [Fact]
    public async Task A_notification_reaches_the_mailbox_too()
    {
        var ownerEmail = Email();
        var owner = await api.SignedInAsync(ownerEmail);
        var organizationId = await OrganizationAsync(owner);

        var joinerEmail = Email();
        var joiner = await api.SignedInAsync(joinerEmail);
        await JoinByInvitationAsync(owner, joiner, organizationId, joinerEmail);

        // Read as a recipient would: the message itself, sent by the sweep that turns
        // notifications into email.
        var message = await api.Mailbox.WaitForAsync(
            ownerEmail, "joined", TestContext.Current.CancellationToken);

        // The title is the subject; the body carries the summary and a link straight to the
        // page the notification is about.
        message.Subject.ShouldContain(NameOf(joinerEmail));
        message.TextBody.ShouldContain($"/orgs/{organizationId}/members");

        // And how to stop receiving it — in the body for a reader, and in the header for the
        // mail client, which is what puts an Unsubscribe control beside the message.
        message.TextBody.ShouldContain("/account/notifications");
        message.UnsubscribeUrl.ShouldNotBeNull().ShouldEndWith("/account/notifications");
    }

    [Fact]
    public async Task Reading_them_all_clears_the_count()
    {
        var owner = await api.SignedInAsync(Email());
        var organizationId = await OrganizationAsync(owner);

        var joinerEmail = Email();
        var joiner = await api.SignedInAsync(joinerEmail);
        await JoinByInvitationAsync(owner, joiner, organizationId, joinerEmail);

        (await FeedAsync(owner)).UnreadCount.ShouldBe(1);

        var read = await owner.PostAsync(
            new Uri("/api/v1/notifications/read-all", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        read.EnsureSuccessStatusCode();

        var after = await FeedAsync(owner);
        after.UnreadCount.ShouldBe(0);
        after.Items.ShouldNotBeEmpty("read is not deleted");
        after.Items.ShouldAllBe(n => n.ReadAt != null);
    }

    /// <summary>Invites an address, reads the token out of the mail, and accepts it.</summary>
    private async Task<Guid> JoinByInvitationAsync(
        HttpClient inviter,
        HttpClient invitee,
        Guid organizationId,
        string email,
        OrganizationRole role = OrganizationRole.Member)
    {
        var invited = await inviter.PostAsJsonAsync(
            new Uri($"/api/v1/organizations/{organizationId}/members", UriKind.Relative),
            new { email, role = (int)role },
            TestContext.Current.CancellationToken);

        invited.EnsureSuccessStatusCode();

        var invitation = await api.Mailbox.WaitForAsync(
            email, "invited", TestContext.Current.CancellationToken);

        var match = Regex.Match(invitation.TextBody, @"token=([A-Za-z0-9%]+)");
        match.Success.ShouldBeTrue("the invitation email carries an accept link");

        var accepted = await invitee.PostAsJsonAsync(
            "/api/v1/invitations/accept",
            new { token = Uri.UnescapeDataString(match.Groups[1].Value) },
            TestContext.Current.CancellationToken);

        accepted.EnsureSuccessStatusCode();

        return (await accepted.Content.ReadFromJsonAsync<MemberPayload>(
            TestContext.Current.CancellationToken))!.UserId;
    }

    private static async Task<Feed> FeedAsync(HttpClient client)
    {
        var response = await client.GetAsync(
            new Uri("/api/v1/notifications", UriKind.Relative), TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Feed>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<List<Preference>> PreferencesAsync(HttpClient client)
    {
        var response = await client.GetAsync(
            new Uri("/api/v1/notifications/preferences", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<List<Preference>>(
            TestContext.Current.CancellationToken))!;
    }

    private static string Email() => $"{Guid.NewGuid():N}@example.test";

    /// <summary>
    /// How the notification will name somebody. The harness registers every account with a
    /// display name, and a display name is what a notification uses — showing the address
    /// instead would put a colleague's email in front of everyone who can read the list.
    /// </summary>
    private static string NameOf(string email) => email.Split('@')[0];

    /// <summary>The organization, and the name its links are spelled with.</summary>
    private static async Task<(Guid Id, string Slug)> OrganizationWithSlugAsync(HttpClient client)
    {
        var created = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new { name = $"Org{Guid.NewGuid():N}"[..14] },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();

        var payload = await created.Content.ReadFromJsonAsync<SlugPayload>(
            TestContext.Current.CancellationToken);

        return (payload!.Id, payload.Slug);
    }

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

    private sealed record SlugPayload(Guid Id, string Slug);

    private sealed record MemberPayload(Guid UserId);

    private sealed record Feed(int UnreadCount, List<Item> Items);

    private sealed record Item(
        Guid Id, string Kind, string Title, string Summary, string? Detail, string Link,
        DateTimeOffset? ReadAt);

    private sealed record Preference(string Kind, string Channel, bool Enabled);
}
