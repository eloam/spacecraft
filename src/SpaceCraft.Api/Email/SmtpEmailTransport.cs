using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Polly;
using SpaceCraft.Api.Common;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Api.Email;

/// <summary>
/// Delivers over SMTP.
/// <para>
/// The sending address must belong to a domain the relay has been shown to own — SPF, DKIM
/// and MX records — or every message is rejected. That is a deployment prerequisite, not
/// something configuration can work around.
/// </para>
/// <para>
/// Message bodies are never logged. A password reset link is a bearer credential, and an
/// error path that dumps the body would put it in the log store anyway, which is exactly
/// what moving off the logging transport was meant to stop.
/// </para>
/// </summary>
public sealed class SmtpEmailTransport(
    [FromKeyedServices(SmtpEmailTransport.PipelineKey)] ResiliencePipeline pipeline,
    IOptions<SmtpEmailOptions> options,
    ILogger<SmtpEmailTransport> logger) : IEmailTransport
{
    /// <summary>How the composition root and this transport agree on one pipeline.</summary>
    public const string PipelineKey = "email-smtp";

    private readonly SmtpEmailOptions _options = options.Value;

    // Non-null by construction: the composition root only registers this transport once the
    // relay is configured. Read into fields so that a gap fails when the transport is
    // resolved rather than at the first password reset somebody is waiting on.
    private readonly string _host = Required(options.Value.Host, "Email:Smtp:Host");
    private readonly string _username = Required(options.Value.Username, "Email:Smtp:Username");
    private readonly string _password = Required(options.Value.Password, "Email:Smtp:Password");

    private static string Required(string? value, string key) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"{key} is required to send over SMTP.");

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var mail = new MimeMessage();
        mail.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
        mail.To.Add(MailboxAddress.Parse(message.To));
        mail.Subject = message.Subject;
        mail.Body = new BodyBuilder
        {
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody,
        }.ToMessageBody();

        try
        {
            // Retries, backoff with jitter and the per-attempt timeout live in the pipeline,
            // not here. A fresh connection per attempt on purpose: MailKit's client holds one
            // socket and is not safe to share, and a relay that dropped us mid-send leaves a
            // client that cannot be reused anyway.
            await pipeline.ExecuteAsync(
                async token => await DeliverAsync(mail, token), ct);

            logger.LogInformation("Email sent to {To}: {Subject}", message.To, message.Subject);
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException
                                      or AuthenticationException or IOException
                                      or OperationCanceledException)
        {
            // Named exception types rather than a bare catch: a bug in message construction
            // should surface as a bug, not as "the mail provider is having trouble".
            logger.LogError(
                ex, "Email to {To} could not be delivered: {Subject}", message.To, message.Subject);
            throw;
        }
    }

    private async Task DeliverAsync(MimeMessage mail, CancellationToken ct)
    {
        using var client = new SmtpClient
        {
            ServerCertificateValidationCallback = (_, _, chain, errors) =>
                IsAcceptable(errors, chain?.ChainStatus, _options.RequireCertificateRevocation),
        };

        // SecureSocketOptions.Auto is deliberately absent: it falls back to plaintext against
        // a relay that fails to advertise STARTTLS and sends the password in the clear. Going
        // without encryption is a configured decision here, never a fallback.
        var security = _options.Security switch
        {
            SmtpSecurity.ImplicitTls => SecureSocketOptions.SslOnConnect,
            SmtpSecurity.None => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls,
        };

        await client.ConnectAsync(_host, _options.Port, security, ct);

        // A development inbox accepts anything and advertises no AUTH at all; offering
        // credentials to one throws rather than being ignored.
        if (client.Capabilities.HasFlag(SmtpCapabilities.Authentication))
        {
            await client.AuthenticateAsync(_username, _password, ct);
        }

        await client.SendAsync(mail, ct);

        // Quit properly rather than dropping the socket: a relay counts an abandoned
        // connection against the sender, and some rate-limit on it.
        await client.DisconnectAsync(true, ct);
    }

    /// <summary>
    /// Full certificate validation, and — unless
    /// <see cref="SmtpEmailOptions.RequireCertificateRevocation"/> says otherwise — one
    /// exception: a revocation check that could not be carried out.
    ///
    /// <para>
    /// Revocation is always requested. What the setting decides is what happens when no
    /// answer comes back, which on macOS and in a container without a route to an OCSP
    /// responder is every time. Nothing else moves: an expired certificate, an untrusted
    /// root, a mismatched name and a revocation that came back positive are refused in
    /// both modes.
    /// </para>
    /// </summary>
    /// <remarks>Public so the rule can be tested; there is no other caller.</remarks>
    public static bool IsAcceptable(
        SslPolicyErrors errors, X509ChainStatus[]? chainStatus, bool requireRevocation)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        // A missing certificate or a mismatched name is never negotiable.
        if (requireRevocation || errors != SslPolicyErrors.RemoteCertificateChainErrors)
        {
            return false;
        }

        return chainStatus is { Length: > 0 } && chainStatus.All(status =>
            status.Status is X509ChainStatusFlags.RevocationStatusUnknown
                or X509ChainStatusFlags.OfflineRevocation);
    }
}
