using System.Security.Cryptography;
using System.Text;

namespace SpaceCraft.Api.Email;

/// <summary>
/// Token generation and hashing for waitlist confirmations, shared between the endpoint that
/// accepts a signup and the dispatcher that records it.
/// </summary>
public static class WaitlistTokens
{
    public static string Generate() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>
    /// Stores a hash rather than the address itself: enough to rate-limit repeat abuse,
    /// without retaining a personal identifier for everyone who visits the landing page.
    /// </summary>
    public static string? HashIp(string? ip) =>
        string.IsNullOrWhiteSpace(ip) ? null : Hash(ip);
}
