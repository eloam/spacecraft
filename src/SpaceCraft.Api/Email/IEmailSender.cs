namespace SpaceCraft.Api.Email;

/// <summary>
/// An outgoing message. Both bodies are sent: clients that render HTML use it, and the text
/// part is what plain-text readers, screen readers and spam filters see. Sending HTML alone
/// is a reliable way to score badly with the latter.
/// </summary>
/// <param name="To">The recipient's address.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="TextBody">The plain-text part.</param>
/// <param name="HtmlBody">The HTML part, when there is one.</param>
/// <param name="UnsubscribeUrl">
/// Where the reader turns this kind of message off, published as <c>List-Unsubscribe</c>.
/// Set it on anything somebody chose to receive and can stop; leave it unset on a password
/// reset or an invitation, which are answers to a request rather than a subscription.
/// </param>
public sealed record EmailMessage(
    string To,
    string Subject,
    string TextBody,
    string? HtmlBody = null,
    string? UnsubscribeUrl = null)
{
    /// <summary>
    /// What a log line calls this message.
    /// </summary>
    /// <remarks>
    /// Never the address, and not the subject either: a notification's title carries the
    /// display name of whoever acted, which is their address when they set no name. Every
    /// producer puts an address in here on the understanding that it does not reach the log
    /// — the reset dispatcher and the queue both keep their own lines free of it — and the
    /// transport's delivery report was the one place it did. A key minted here rather than
    /// borrowed from the notification or the reset it carries, because the message does not
    /// know which it is and a transport should not have to ask. The producer that does know
    /// logs the same key beside its own, which is what makes the two lines one story.
    /// </remarks>
    public Guid Id { get; init; } = Guid.CreateVersion7();
}

/// <summary>
/// What endpoints call to send mail. Implementations are expected to return promptly and
/// without revealing whether delivery succeeded — see
/// <c>SpaceCraft.Api.Email.QueuedEmailSender</c> for why that matters.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
