using SpaceCraft.Domain.Common;

namespace SpaceCraft.Domain.Notifications;

/// <summary>
/// One person's answer for one kind of event on one channel.
/// </summary>
/// <remarks>
/// A row exists only where somebody has said something; everything else falls back to
/// <see cref="NotificationDefaults"/>. That keeps a new kind or a new channel working the day
/// it ships instead of the day everyone re-saves their settings.
/// <para>
/// Held per account rather than per organization: whether you want an email is a fact about
/// you, not about the team. Should that ever need to differ per organization, this row grows
/// a nullable organization id and the lookup prefers the specific over the general.
/// </para>
/// </remarks>
public sealed class NotificationPreference : Entity
{
    private NotificationPreference() { }

    public NotificationPreference(
        Guid userId, NotificationKind kind, NotificationChannel channel, bool enabled)
    {
        UserId = userId;
        Kind = kind;
        Channel = channel;
        Enabled = enabled;
    }

    public Guid UserId { get; private set; }

    public NotificationKind Kind { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public bool Enabled { get; private set; }

    public void Set(bool enabled)
    {
        Enabled = enabled;
        Touch();
    }
}
