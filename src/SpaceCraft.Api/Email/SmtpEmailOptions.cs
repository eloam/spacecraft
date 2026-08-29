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
public sealed class SmtpEmailOptions
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
    /// Left empty for Scaleway: the composition root fills it from <c>Scaleway:ProjectId</c>,
    /// because that is what Scaleway uses as an SMTP username and the id already lives in one
    /// place for the whole account. Any other relay sets this explicitly and wins.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Secret key of an IAM API key with Transactional Email write permission, for Scaleway.
    /// Never logged, and never included in an error message.
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

    /// <summary>Ceiling for one delivery including every retry.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Ceiling for a single attempt, before the pipeline retries it.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>Retries after the first attempt.</summary>
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// A relay to talk to and a password to say it with. The username is not part of this:
    /// it has a default, and a blank one would only be caught at the first send.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Password);
}
