namespace SpaceCraft.Domain.Notifications;

/// <summary>
/// What somebody receives before they have said otherwise.
/// </summary>
/// <remarks>
/// Everything is on. A notification system that arrives silent looks broken, and the settings
/// page exists precisely so that anyone finding it noisy can turn it down — which is a
/// cheaper mistake than a run that failed overnight and told nobody.
/// <para>
/// Absence of a preference row means "whatever this says", so defaults can change later
/// without a data migration, and a new kind or channel starts working the day it is added.
/// </para>
/// </remarks>
public static class NotificationDefaults
{
    public static bool IsEnabled(NotificationKind kind, NotificationChannel channel) =>
        (kind, channel) switch
        {
            _ => true,
        };

    /// <summary>Every kind, in the order the settings page lists them.</summary>
    public static IReadOnlyList<NotificationKind> Kinds { get; } =
    [
        NotificationKind.MemberJoined,
        NotificationKind.MemberLeft,
        NotificationKind.RunCompleted,
        NotificationKind.RemovedFromOrganization,
        NotificationKind.InvitationDeclined,
    ];

    /// <summary>Every channel that actually delivers today.</summary>
    public static IReadOnlyList<NotificationChannel> Channels { get; } =
    [
        NotificationChannel.InApp,
        NotificationChannel.Email,
    ];
}
