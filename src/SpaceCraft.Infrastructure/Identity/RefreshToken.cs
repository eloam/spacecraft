using SpaceCraft.Domain.Common;

namespace SpaceCraft.Infrastructure.Identity;

/// <summary>
/// A long-lived session token, stored hashed.
/// <para>
/// Access tokens are deliberately short-lived and stateless; this is the piece of state
/// that makes logout and revocation actually work. Rotation is enforced: redeeming a token
/// issues a new one and marks the old one used, so a stolen token is detectable — if a
/// used token is presented again, the whole family is revoked.
/// </para>
/// </summary>
public sealed class RefreshToken : Entity
{
    private RefreshToken() { }

    public RefreshToken(Guid userId, string tokenHash, DateTimeOffset expiresAt, Guid? familyId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        FamilyId = familyId ?? Ids.New();
    }

    public Guid UserId { get; private set; }

    /// <summary>SHA-256 hex of the token. The raw value only ever exists in the response body.</summary>
    public string TokenHash { get; private set; } = null!;

    /// <summary>Groups every token descended from one login, so a breach revokes the chain.</summary>
    public Guid FamilyId { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => UsedAt is null && RevokedAt is null && ExpiresAt > now;

    public void MarkUsed(DateTimeOffset at)
    {
        UsedAt = at;
        Touch();
    }

    public void Revoke(DateTimeOffset at)
    {
        RevokedAt ??= at;
        Touch();
    }
}
