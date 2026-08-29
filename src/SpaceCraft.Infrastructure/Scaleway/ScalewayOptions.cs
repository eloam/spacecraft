namespace SpaceCraft.Infrastructure.Scaleway;

/// <summary>
/// Facts about the Scaleway account itself, kept in one place rather than filed under a
/// service: the project a sending domain belongs to is the same project the buckets belong
/// to, and filing it under the mail settings made it look like a mail setting.
///
/// <para>
/// Nothing binds this today. Mail moved to SMTP, where the login is written down rather than
/// derived from a provider, and Object Storage authenticates with its own key pair — so the
/// project id is now a fact a person needs when configuring the console, not one the
/// application reads. It is kept as the record of which project everything lives in.
/// </para>
///
/// API keys stay per service, one IAM application each, so a key that can send mail cannot
/// also read the bucket.
/// </summary>
public sealed class ScalewayOptions
{
    public const string SectionName = "Scaleway";

    /// <summary>The Scaleway Project the sending domain and the buckets live in.</summary>
    public string? ProjectId { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProjectId);
}
