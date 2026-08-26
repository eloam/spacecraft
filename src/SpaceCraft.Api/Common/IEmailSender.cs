namespace SpaceCraft.Api.Common;

/// <summary>
/// An outgoing message. Both bodies are sent: clients that render HTML use it, and the text
/// part is what plain-text readers, screen readers and spam filters see. Sending HTML alone
/// is a reliable way to score badly with the latter.
/// </summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);

/// <summary>
/// What endpoints call to send mail. Implementations are expected to return promptly and
/// without revealing whether delivery succeeded — see
/// <c>SpaceCraft.Api.Email.QueuedEmailSender</c> for why that matters.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
