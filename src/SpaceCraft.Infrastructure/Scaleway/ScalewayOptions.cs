namespace SpaceCraft.Infrastructure.Scaleway;

/// <summary>
/// Facts about the Scaleway account itself, shared by every service that talks to it.
///
/// These are deliberately not filed under one service. The project a sending domain belongs
/// to is the same project the buckets belong to; filing it under the mail settings made it
/// look like a mail setting and left the next Scaleway-facing feature to ask for it again
/// somewhere else.
///
/// It is also Scaleway's SMTP username, which is why the mail transport reads it from here
/// rather than asking for it twice.
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
