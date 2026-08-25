using SpaceCraft.Domain.Common;

namespace SpaceCraft.Domain.Organizations;

/// <summary>
/// An email captured by the "Notify me" form while the product is pre-launch.
/// Confirmation is double opt-in: an address only counts as subscribed once the
/// emailed token comes back, which keeps typo'd and maliciously-submitted addresses
/// out of the launch send.
/// </summary>
public sealed class WaitlistEntry : Entity
{
    private WaitlistEntry() { }

    public WaitlistEntry(string email, string confirmationTokenHash, string? source, string? ipHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmationTokenHash);

        Email = email.Trim().ToLowerInvariant();
        ConfirmationTokenHash = confirmationTokenHash;
        Source = source;
        IpHash = ipHash;
    }

    public string Email { get; private set; } = null!;

    /// <summary>SHA-256 of the confirmation token; the token itself only ever goes in the email.</summary>
    public string ConfirmationTokenHash { get; private set; } = null!;

    public bool IsConfirmed { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }

    /// <summary>Where the signup came from, e.g. "landing-hero" or "landing-footer".</summary>
    public string? Source { get; private set; }

    /// <summary>Hashed client IP, kept only for abuse rate-limiting — never the raw address.</summary>
    public string? IpHash { get; private set; }

    public DateTimeOffset? UnsubscribedAt { get; private set; }

    public bool IsActive => IsConfirmed && UnsubscribedAt is null;

    public void Confirm(DateTimeOffset at)
    {
        if (IsConfirmed)
        {
            return;
        }

        IsConfirmed = true;
        ConfirmedAt = at;
        Touch();
    }

    public void Unsubscribe(DateTimeOffset at)
    {
        UnsubscribedAt = at;
        Touch();
    }

    /// <summary>Issues a fresh token when someone asks for the confirmation email again.</summary>
    public void ReissueToken(string confirmationTokenHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmationTokenHash);
        ConfirmationTokenHash = confirmationTokenHash;
        Touch();
    }
}
