using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Organizations;

namespace SpaceCraft.Infrastructure.Identity;

/// <summary>
/// A grant to join an organization, sent to an address and redeemed by whoever reads it.
/// </summary>
/// <remarks>
/// Membership used to be handed out by resolving an address to whatever account already used
/// it. Nothing in this system proves an address belongs to the account holding it —
/// <c>EmailConfirmed</c> is written by a migration and read by nobody — so registering
/// somebody's work address was enough to be given their organization the day a colleague
/// invited them. The invitation closes that by moving the proof to where it belongs: the
/// token goes to the mailbox, and only someone who can read that mailbox can accept.
/// <para>
/// Stored hashed, like <see cref="PasswordResetToken"/> and <see cref="RefreshToken"/>: the
/// raw value exists only in the email, so a database disclosure hands over no memberships.
/// </para>
/// </remarks>
public sealed class OrganizationInvitation : Entity
{
    private OrganizationInvitation() { }

    public OrganizationInvitation(
        Guid organizationId,
        string email,
        OrganizationRole role,
        Guid invitedByUserId,
        string tokenHash,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        OrganizationId = organizationId;
        Email = email.Trim().ToLowerInvariant();
        Role = role;
        InvitedByUserId = invitedByUserId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public Guid OrganizationId { get; private set; }

    /// <summary>
    /// The address the invitation was sent to, lowercased. Kept for the listing an admin
    /// reads, and to say who an outstanding invitation is for — not to identify the account
    /// that eventually accepts, which is whoever holds the token.
    /// </summary>
    public string Email { get; private set; } = null!;

    public OrganizationRole Role { get; private set; }

    public Guid InvitedByUserId { get; private set; }

    /// <summary>SHA-256 hex of the token. The raw value is never persisted.</summary>
    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    /// <summary>The account that accepted, once one has.</summary>
    public Guid? AcceptedByUserId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsRedeemable(DateTimeOffset now) =>
        AcceptedAt is null && RevokedAt is null && ExpiresAt > now;

    public void Accept(Guid userId, DateTimeOffset at)
    {
        if (AcceptedAt is not null)
        {
            throw new InvalidOperationException("This invitation has already been accepted.");
        }

        AcceptedAt = at;
        AcceptedByUserId = userId;
        Touch();
    }

    /// <summary>
    /// Puts the invitation out, whether because it was withdrawn or because a newer one
    /// replaced it. Idempotent, so re-inviting twice does not fight itself.
    /// </summary>
    public void Revoke(DateTimeOffset at)
    {
        RevokedAt ??= at;
        Touch();
    }
}
