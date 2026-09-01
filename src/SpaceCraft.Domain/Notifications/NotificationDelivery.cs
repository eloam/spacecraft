using SpaceCraft.Domain.Common;

namespace SpaceCraft.Domain.Notifications;

/// <summary>
/// One attempt to push a notification out through one channel.
/// </summary>
/// <remarks>
/// A row per channel rather than a column per channel. The first shape of this held
/// <c>email_requested</c>, <c>email_sent_at</c> and <c>email_attempts</c> on the notification
/// itself, which meant Slack would have cost three more columns and a migration, and the next
/// channel three more again — a table that widens with every integration, and a sweep that
/// has to be written once per channel.
/// <para>
/// The in-app list is deliberately not here. It has nothing to deliver: the notification's own
/// row is the delivery, it cannot fail, and there is nothing to retry. What this table holds
/// is outbound transports — the ones that talk to somebody else's server and can be refused.
/// </para>
/// </remarks>
public sealed class NotificationDelivery : Entity
{
    /// <summary>
    /// Attempts before a message is left alone. A relay that keeps rejecting an address will
    /// keep rejecting it, and a queue that retries forever is a queue that never drains.
    /// </summary>
    public const int MaxAttempts = 3;

    private NotificationDelivery() { }

    public NotificationDelivery(Guid notificationId, NotificationChannel channel)
    {
        NotificationId = notificationId;
        Channel = channel;
    }

    public Guid NotificationId { get; private set; }

    public NotificationChannel Channel { get; private set; }

    /// <summary>
    /// When the message was handed to the transport — and, before that, the claim.
    /// </summary>
    /// <remarks>
    /// Stamped before the send rather than after it. Reading a row, sending, and then marking
    /// it sent is two operations with a gap, and a second instance sweeping inside that gap
    /// sends the same message twice. Claiming first means a crash in the gap loses a message
    /// instead of duplicating it, which is the right way round for something that lands in
    /// somebody's inbox.
    /// </remarks>
    public DateTimeOffset? SentAt { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>Whether this has been tried as often as it is going to be.</summary>
    public bool IsAbandoned => SentAt is null && Attempts >= MaxAttempts;

    /// <summary>
    /// Takes the row, before anything is sent. Counting the attempt here rather than after a
    /// failure is what stops a message that kills the process from being retried for ever.
    /// </summary>
    public void Claim(DateTimeOffset at)
    {
        SentAt = at;
        Attempts++;
        Touch();
    }

    /// <summary>
    /// Puts it back for the next sweep. The attempt stays counted, so a recipient the relay
    /// keeps rejecting is given up on rather than retried until the end of time.
    /// </summary>
    public void Release()
    {
        SentAt = null;
        Touch();
    }
}
