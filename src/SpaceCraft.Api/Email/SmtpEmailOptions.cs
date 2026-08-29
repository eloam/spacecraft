using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Api.Email;

/// <summary>
/// Everything needed to hand a message to an SMTP relay.
///
/// Deliberately says nothing about who runs that relay. SMTP is the one part of this
/// deployment that is a protocol rather than a product: moving from Scaleway to Postmark,
/// SES or a self-hosted Postfix is four settings and no code. That is why the transport
/// speaks SMTP and not a provider's REST API.
/// </summary>
public sealed class SmtpEmailOptions : IValidatableObject
{
    public const string SectionName = "Email:Smtp";

    /// <summary>
    /// Relay hostname — <c>smtp.tem.scaleway.com</c> for Scaleway Transactional Email. When
    /// this is empty the application falls back to writing emails to the log, which is what
    /// keeps a developer checkout working without credentials.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// 587 or 2587 for STARTTLS, 465 or 2465 for implicit TLS. Port 25 is accepted by the
    /// relay but blocked by most hosting providers on egress, so it is not the default.
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    /// <summary>
    /// True for implicit TLS, where the connection is encrypted before the first byte of
    /// SMTP. False for STARTTLS, which upgrades a plaintext connection.
    ///
    /// Either is encrypted; what is refused in both cases is falling back to plaintext when
    /// the upgrade fails, which is how an SMTP client silently leaks a password.
    /// </summary>
    public bool UseImplicitTls { get; set; }

    /// <summary>
    /// SMTP login. Scaleway wants the id of the project the sending domain belongs to, other
    /// relays want an address or an account name — which is exactly why it is written here
    /// rather than derived from something provider-shaped.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// SMTP password — an API key's secret for a relay that authenticates that way, an
    /// account password for one that does not. Never logged, and never included in an error
    /// message.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Sender address. A relay rejects anything outside a domain it has been shown to own —
    /// SPF, DKIM and MX records — so this is not free-form.
    /// </summary>
    [Required]
    [EmailAddress]
    public string FromEmail { get; set; } = "noreply@spacecraft.dev";

    [Required]
    public string FromName { get; set; } = "SpaceCraft";

    /// <summary>
    /// Whether the relay's certificate must come with a positive answer on revocation.
    ///
    /// <para>
    /// On, because a deployment that cannot get an answer about its relay's certificate
    /// should say so rather than shrug. Development turns it off: .NET on macOS, and any
    /// container with no route to an OCSP responder, never completes the check, so a
    /// perfectly good certificate is rejected and no mail leaves the machine.
    /// </para>
    ///
    /// <para>
    /// Revocation is requested in both modes. What this decides is only what happens when no
    /// answer comes back — an expired certificate, an untrusted root, a mismatched name and a
    /// revocation that comes back positive are refused either way.
    /// </para>
    /// </summary>
    public bool RequireCertificateRevocation { get; set; } = true;

    /// <summary>Ceiling for one delivery including every retry.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Ceiling for a single attempt, before the pipeline retries it.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>Retries after the first attempt.</summary>
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 3;

    /// <summary>All three, or there is no relay to talk to.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password);

    /// <summary>
    /// None of the three settings can be <c>[Required]</c> on its own: all of them blank is
    /// the legitimate state of a checkout with no relay, and what selects the transport that
    /// writes to the log.
    ///
    /// <para>
    /// Some of them blank is a different thing entirely — somebody was configuring a relay
    /// and stopped. Read as "not configured" it would swallow every message the application
    /// sends, silently, which is why it is rejected here instead. Runs under the same
    /// ValidateDataAnnotations as the attributes above.
    /// </para>
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        string[] names = [nameof(Host), nameof(Username), nameof(Password)];
        var missing = new[] { Host, Username, Password }
            .Select((value, index) => (Name: names[index], Blank: string.IsNullOrWhiteSpace(value)))
            .Where(setting => setting.Blank)
            .Select(setting => setting.Name)
            .ToList();

        // A port and a handshake that contradict each other produce no error and no mail: the
        // client waits for a greeting the server will not send until it sees TLS, or offers
        // TLS to a server already speaking it. It ends as a timeout, several retries later,
        // with nothing naming the cause.
        int[] implicitTlsPorts = [465, 2465];

        if (implicitTlsPorts.Contains(Port) != UseImplicitTls)
        {
            yield return new ValidationResult(
                UseImplicitTls
                    ? $"Email:Smtp:UseImplicitTls is on, which belongs to port 465 or 2465, not {Port}. "
                      + "Ports 25, 587 and 2587 upgrade with STARTTLS — turn it off."
                    : $"Email:Smtp:Port {Port} expects TLS from the first byte. "
                      + "Set UseImplicitTls to true, or use 587 to upgrade with STARTTLS.",
                [nameof(Port), nameof(UseImplicitTls)]);
        }

        if (missing.Count is > 0 and < 3)
        {
            yield return new ValidationResult(
                $"Email:Smtp is half configured: {string.Join(" and ", missing)} "
                + $"{(missing.Count == 1 ? "is" : "are")} missing. Supply all of "
                + "Host, Username and Password, or none of them to log messages instead.",
                names);
        }
    }
}
