using System.Security.Cryptography;
using System.Text;

namespace SpaceCraft.Api.Common;

/// <summary>
/// Generation and hashing for every token that is handed out raw and looked up by its hash:
/// refresh tokens, password reset links, invitations and waitlist confirmations.
/// </summary>
/// <remarks>
/// They all need the same thing for the same reason: a value with enough entropy that
/// guessing is hopeless, stored as a hash so a database disclosure hands over nothing, and
/// compared by lookup rather than by string so the comparison has no timing to leak. This
/// used to exist three times over, with the mailed ones in hex and the others in base64url,
/// which was two ways of spelling 256 bits. Hex is kept: it needs no escaping in a link or a
/// JSON body, and a hash on a row is the hash of whatever raw string was issued, so tokens
/// issued before the change still redeem.
/// </remarks>
public static class OpaqueTokens
{
    /// <summary>256 bits from the system's CSPRNG, as 64 lowercase hex characters.</summary>
    public static string Generate() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    /// <summary>SHA-256 of a token, for storage and lookup. Raw tokens are never persisted.</summary>
    public static string Hash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
