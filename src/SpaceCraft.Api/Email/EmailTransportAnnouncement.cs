using Microsoft.Extensions.Options;

namespace SpaceCraft.Api.Email;

/// <summary>
/// Says which transport is live, once, at startup.
///
/// <para>
/// The two failure modes look identical from outside — nothing arrives — so the first
/// question when a message goes missing is "is it even trying to send?", and until this
/// existed nothing in the logs answered it.
/// </para>
/// </summary>
public sealed class EmailTransportAnnouncement(
    IEmailTransport transport,
    IOptions<SmtpEmailOptions> options,
    ILogger<EmailTransportAnnouncement> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (transport is SmtpEmailTransport)
        {
            // The password is not among these, and the username is not a secret: it is the
            // first thing to check when a relay refuses the login.
            logger.LogInformation(
                "Email is sent over SMTP via {Host}:{Port} as {Username} using {Security}",
                settings.Host, settings.Port, settings.Username, settings.Security);

            if (settings.Security == SmtpSecurity.None)
            {
                // Loud, every start: the credentials and every message cross the network
                // readable. Development is the only place this is a reasonable trade.
                logger.LogWarning(
                    "SMTP is unencrypted. Email:Smtp:Security is None, which is only meant "
                    + "for a relay on this machine.");
            }
        }
        else
        {
            logger.LogWarning(
                "Email is written to the log rather than sent: {Transport} is the configured "
                + "transport. Set Email:Smtp:Host, Username and Password to deliver.",
                transport.GetType().Name);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
