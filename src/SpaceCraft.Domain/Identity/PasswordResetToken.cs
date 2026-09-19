using SpaceCraft.Domain.Common;

namespace SpaceCraft.Domain.Identity;

/// <summary>
/// A single-use, short-lived grant to choose a new password, stored hashed.
/// <para>
/// The raw token only ever exists in the email that carries it. Storing the hash means a
/// database disclosure does not hand over the ability to take over accounts — the same
/// reasoning as <see cref="RefreshToken"/>.
/// </para>
/// <para>
/// Requesting a reset invalidates any earlier outstanding token for that account, so a
/// forwarded or intercepted older email stops working the moment a newer one is asked for.
/// </para>
/// </summary>
public sealed class PasswordResetToken : Entity
{
    private PasswordResetToken() { }

    public PasswordResetToken(Guid userId, string tokenHash, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }

    /// <summary>SHA-256 hex of the token. The raw value is never persisted.</summary>
    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset? InvalidatedAt { get; private set; }

    public bool IsRedeemable(DateTimeOffset now) =>
        UsedAt is null && InvalidatedAt is null && ExpiresAt > now;

    public void MarkUsed(DateTimeOffset at)
    {
        UsedAt = at;
        Touch();
    }

    /// <summary>Superseded by a newer request, or cancelled because the password changed.</summary>
    public void Invalidate(DateTimeOffset at)
    {
        InvalidatedAt ??= at;
        Touch();
    }
}
