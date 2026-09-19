namespace SpaceCraft.Api.Email;

/// <summary>
/// An outgoing message. Both bodies are sent: clients that render HTML use it, and the text
/// part is what plain-text readers, screen readers and spam filters see. Sending HTML alone
/// is a reliable way to score badly with the latter.
/// </summary>
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
    string? UnsubscribeUrl = null);

/// <summary>
/// What endpoints call to send mail. Implementations are expected to return promptly and
/// without revealing whether delivery succeeded — see
/// <c>SpaceCraft.Api.Email.QueuedEmailSender</c> for why that matters.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
