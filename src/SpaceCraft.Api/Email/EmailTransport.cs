
namespace SpaceCraft.Api.Email;

/// <summary>
/// Actually delivers a message. Separate from <see cref="IEmailSender"/>, which is what
/// endpoints call: they hand a message over and return, while delivery happens behind them.
/// </summary>
public interface IEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>
/// Development transport: writes the message to the log instead of delivering it.
/// <para>
/// Bodies are logged in full here on purpose — that is how a developer follows a reset link
/// without a mail provider. It also means logs contain live credentials, so this transport
/// must never be the one running in a deployed environment.
/// </para>
/// </summary>
public sealed class LoggingEmailTransport(ILogger<LoggingEmailTransport> logger) : IEmailTransport
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
