using Microsoft.Extensions.Logging;

namespace SpaceCraft.Api.Common;

public sealed record EmailMessage(string To, string Subject, string TextBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>
/// Development sink: writes the message to the log instead of sending it.
/// <para>
/// Swap for a real transport (Postmark, SES, Resend) before launch — the waitlist
/// double opt-in flow is inert without one.
/// </para>
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        logger.LogInformation(
            "[email not sent — no transport configured] To: {To} | Subject: {Subject}\n{Body}",
            message.To, message.Subject, message.TextBody);

        return Task.CompletedTask;
    }
}
