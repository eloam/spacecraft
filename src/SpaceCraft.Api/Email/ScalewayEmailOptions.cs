using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Api.Email;

public sealed class ScalewayEmailOptions
{
    public const string SectionName = "Email:Scaleway";

    /// <summary>
    /// Secret key of an IAM API key with Transactional Email write permission. When this is
    /// empty the application falls back to writing emails to the log, which is what keeps a
    /// developer checkout working without credentials.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Transactional Email is Paris-only today, but the API is region-scoped.</summary>
    [Required]
    public string Region { get; set; } = "fr-par";

    /// <summary>
    /// Sender address. Scaleway rejects anything outside a domain verified in the console
    /// (SPF, DKIM and MX records), so this is not free-form.
    /// </summary>
    [Required]
    [EmailAddress]
    public string FromEmail { get; set; } = "noreply@spacecraft.dev";

    [Required]
    public string FromName { get; set; } = "SpaceCraft";

    /// <summary>
    /// API root. Overridable so the transport can be pointed at a local stub in tests, and
    /// so a deployment behind an egress proxy has somewhere to point.
    /// </summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = "https://api.scaleway.com";

    /// <summary>Ceiling for one delivery including every retry.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Ceiling for a single attempt, before the pipeline retries it.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>Retries after the first attempt.</summary>
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// The key alone. The project id this transport also needs is an account-level fact and
    /// lives in <c>Scaleway:ProjectId</c>; the composition root checks both.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
