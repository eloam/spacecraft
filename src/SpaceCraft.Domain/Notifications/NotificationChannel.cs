namespace SpaceCraft.Domain.Notifications;

/// <summary>
/// How a notification reaches somebody.
/// </summary>
/// <remarks>
/// Both sides of a channel are rows rather than columns — the preference somebody sets, and
/// the delivery that carries the message out. So adding one is a member here, a sender for
/// it, and a block in the settings page: no schema change, and nothing widens. Slack is the
/// next one, and the settings page already says so.
/// <para>
/// Persisted by value, like <see cref="NotificationKind"/>: add at the end, never renumber.
/// </para>
/// </remarks>
public enum NotificationChannel
{
    /// <summary>The notification list inside the application.</summary>
    InApp = 1,

    Email = 2,
}
