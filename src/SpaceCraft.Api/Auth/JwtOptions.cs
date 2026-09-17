using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Api.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    // Sensible defaults, so no [Required]: on a string that carries a non-empty initializer
    // the attribute never fires for a missing key and only misleads a reader. SigningKey below
    // initialises to string.Empty, which is the one initializer [Required] does refuse.
    public string Issuer { get; set; } = "spacecraft";

    public string Audience { get; set; } = "spacecraft-api";

    /// <summary>
    /// Signing key. Must be at least 32 bytes for HMAC-SHA256 and must come from
    /// configuration/secrets, never from source.
    /// </summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Short by design: refresh tokens carry the long-lived session.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);
}
