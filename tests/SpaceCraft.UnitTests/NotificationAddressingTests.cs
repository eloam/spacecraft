using SpaceCraft.Domain.Notifications;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// That a notification reaches a person on exactly the channels they asked for — and that
/// the settings page and the publisher, which both ask, get the same answer.
/// </summary>
public sealed class NotificationAddressingTests
{
    private static readonly Guid Recipient = Guid.CreateVersion7();
    private static readonly Guid Organization = Guid.CreateVersion7();

    private static readonly NotificationContent Content = new(
        NotificationKind.RunCompleted,
        "Run succeeded — sdk",
        "Project, spec #1, TypeScript.",
        null,
        "/orgs/1/runs/1",
        "View the run");

    [Fact]
    public void Somebody_who_has_said_nothing_gets_every_channel()
    {
        var notification = NotificationAddressing.Address(Content, Recipient, Organization, []);

        notification.ShouldNotBeNull();
        notification.UserId.ShouldBe(Recipient);
        notification.OrganizationId.ShouldBe(Organization);
        notification.ShowInApp.ShouldBeTrue();
        notification.Deliveries.Select(d => d.Channel).ShouldBe([NotificationChannel.Email]);
    }

    [Fact]
    public void Saying_no_to_email_keeps_the_in_app_entry_and_drops_the_delivery()
    {
        var stated = new[] { Preference(NotificationKind.RunCompleted, NotificationChannel.Email, enabled: false) };

        var notification = NotificationAddressing.Address(Content, Recipient, Organization, stated);

        notification.ShouldNotBeNull();
        notification.ShowInApp.ShouldBeTrue();
        notification.Deliveries.ShouldBeEmpty();
    }

    [Fact]
    public void Saying_no_to_every_channel_produces_nothing_at_all()
    {
        var stated = NotificationDefaults.Channels
            .Select(channel => Preference(NotificationKind.RunCompleted, channel, enabled: false))
            .ToList();

        NotificationAddressing.Address(Content, Recipient, Organization, stated).ShouldBeNull();
    }

    [Fact]
    public void An_answer_about_another_kind_does_not_apply()
    {
        var stated = new[] { Preference(NotificationKind.MemberJoined, NotificationChannel.Email, enabled: false) };

        NotificationAddressing.IsEnabled(stated, NotificationKind.RunCompleted, NotificationChannel.Email)
            .ShouldBeTrue();
    }

    [Fact]
    public void What_the_settings_page_shows_is_what_the_publisher_does()
    {
        var stated = new[] { Preference(NotificationKind.RunCompleted, NotificationChannel.InApp, enabled: false) };

        var shown = NotificationDefaults.Channels
            .Where(channel => NotificationAddressing.IsEnabled(stated, NotificationKind.RunCompleted, channel))
            .ToList();
        var notification = NotificationAddressing.Address(Content, Recipient, Organization, stated);

        shown.ShouldBe([NotificationChannel.Email]);
        notification.ShouldNotBeNull();
        notification.ShowInApp.ShouldBeFalse();
        notification.Deliveries.Select(d => d.Channel).ShouldBe([NotificationChannel.Email]);
    }

    private static NotificationPreference Preference(
        NotificationKind kind, NotificationChannel channel, bool enabled) =>
        new(Recipient, kind, channel, enabled);
}
