using System.Security.Cryptography;
using System.Text;

namespace SpaceCraft.Api.Email;

/// <summary>
/// Token generation and hashing for anything sent to a mailbox and redeemed by a link.
/// </summary>
/// <remarks>
/// Shared by the waitlist confirmation and the organization invitation, which need the same
/// thing for the same reason: a value with enough entropy that guessing is hopeless, stored
/// as a hash so a database disclosure hands over nothing, and compared by lookup rather than
/// by string so the comparison has no timing to leak.
/// </remarks>
public static class MailedTokens
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
