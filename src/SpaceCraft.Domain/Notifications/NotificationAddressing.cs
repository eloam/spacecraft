using SpaceCraft.Domain.Organizations;

namespace SpaceCraft.Domain.Notifications;

/// <summary>
/// Turns what happened into the notification one person will actually get, on the channels
/// they asked for.
/// </summary>
/// <remarks>
/// The rules live here, away from any query, because two things apply them: the publisher
/// when it raises a notification, and the settings page when it shows the switches. One copy
/// is what stops the page from promising something the publisher does not honour.
/// </remarks>
public static class NotificationAddressing
{
    /// <summary>
    /// The least role that is told about an organization's affairs — its owners and admins,
    /// the people who answer for it.
    /// </summary>
    public const OrganizationRole GovernanceRole = OrganizationRole.Admin;

    /// <summary>
    /// Whether one person receives a kind on a channel: what they said if they said anything,
    /// <see cref="NotificationDefaults"/> otherwise.
    /// </summary>
    /// <param name="stated">
    /// The person's own preference rows. Those for other kinds and channels are ignored, so
    /// the caller may pass everything it has for them.
    /// </param>
    /// <param name="kind">The kind of notification.</param>
    /// <param name="channel">The channel it would go out on.</param>
    public static bool IsEnabled(
        IEnumerable<NotificationPreference> stated, NotificationKind kind, NotificationChannel channel)
    {
        ArgumentNullException.ThrowIfNull(stated);

        return stated.FirstOrDefault(p => p.Kind == kind && p.Channel == channel) is { } preference
            ? preference.Enabled
            : NotificationDefaults.IsEnabled(kind, channel);
    }

    /// <summary>
    /// Addresses content to one person, or returns <see langword="null"/> when they have said
    /// no to every channel: a row nobody would read, in an outbox nothing would send from.
    /// </summary>
    /// <remarks>
    /// Nothing here names a channel. A new one in the enum starts being honoured the day it is
    /// added, for anybody who has not said otherwise.
    /// </remarks>
    /// <param name="content">What the notification says.</param>
    /// <param name="recipient">The person it is for.</param>
    /// <param name="organizationId">The organization it concerns.</param>
    /// <param name="stated">The person's own preference rows, as for <see cref="IsEnabled"/>.</param>
    public static Notification? Address(
        NotificationContent content,
        Guid recipient,
        Guid organizationId,
        IEnumerable<NotificationPreference> stated)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stated);

        var wanted = NotificationDefaults.Channels
            .Where(channel => IsEnabled(stated, content.Kind, channel))
            .ToList();

        if (wanted.Count == 0)
        {
            return null;
        }

        var notification = new Notification(
            recipient,
            organizationId,
            content.Kind,
            content.Title,
            content.Summary,
            content.Detail,
            content.Link,
            content.ActionLabel,
            showInApp: wanted.Contains(NotificationChannel.InApp));

        foreach (var channel in wanted.Where(c => c != NotificationChannel.InApp))
        {
            notification.RequestDelivery(channel);
        }

        return notification;
    }
}
