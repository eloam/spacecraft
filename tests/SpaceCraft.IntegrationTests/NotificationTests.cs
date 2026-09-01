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
        var organizationId = await OrganizationAsync(owner);

        var joinerEmail = Email();
        var joiner = await api.SignedInAsync(joinerEmail);

        await JoinByInvitationAsync(owner, joiner, organizationId, joinerEmail);

        var forOwner = await FeedAsync(owner);
        forOwner.UnreadCount.ShouldBe(1);
        forOwner.Items[0].Kind.ShouldBe("MemberJoined");
        forOwner.Items[0].Title.ShouldContain(NameOf(joinerEmail));
        forOwner.Items[0].Link.ShouldBe($"/orgs/{organizationId}/members");

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
            n.Kind == "MemberLeft" && n.Title.Contains(NameOf(leaverEmail), StringComparison.Ordinal));

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

        // Every one of those items pointed at a page they can no longer open.
        (await FeedAsync(admin)).Items.ShouldBeEmpty();
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
        defaults.Count.ShouldBe(6, "three kinds on two channels");

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
        reloaded.Count.ShouldBe(6, "the repetition wrote one row, not two");
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

    private sealed record MemberPayload(Guid UserId);

    private sealed record Feed(int UnreadCount, List<Item> Items);

    private sealed record Item(
        Guid Id, string Kind, string Title, string Summary, string? Detail, string Link,
        DateTimeOffset? ReadAt);

    private sealed record Preference(string Kind, string Channel, bool Enabled);
}
