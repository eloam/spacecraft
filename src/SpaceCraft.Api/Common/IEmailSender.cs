namespace SpaceCraft.Api.Common;

public sealed record EmailMessage(string To, string Subject, string TextBody);

/// <summary>
/// What endpoints call to send mail. Implementations are expected to return promptly and
/// without revealing whether delivery succeeded — see
/// <c>SpaceCraft.Api.Email.QueuedEmailSender</c> for why that matters.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
