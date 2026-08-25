using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Domain.Registries;

/// <summary>
/// A publish token for one registry, owned by an organization.
/// <para>
/// The plaintext token never lives on this entity. Only the ciphertext is persisted, and
/// only the publish step ever decrypts it — the build step runs with no credentials mounted
/// at all, so a malicious postinstall script in a generated package has nothing to steal.
/// </para>
/// </summary>
public sealed class RegistryCredential : Entity
{
    private RegistryCredential() { }

    public RegistryCredential(
        Guid organizationId,
        RegistryKind registry,
        string label,
        byte[] cipherText,
        string lastFour)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(cipherText);

        OrganizationId = organizationId;
        Registry = registry;
        Label = label;
        CipherText = cipherText;
        LastFour = lastFour;
    }

    public Guid OrganizationId { get; private set; }
    public RegistryKind Registry { get; private set; }

    /// <summary>Human label, e.g. "npm automation token (acme org)".</summary>
    public string Label { get; private set; } = null!;

    /// <summary>Encrypted token bytes. Never returned over the API.</summary>
    public byte[] CipherText { get; private set; } = null!;

    /// <summary>Last four characters of the token, so operators can tell two tokens apart.</summary>
    public string LastFour { get; private set; } = null!;

    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }

    public bool IsUsable(DateTimeOffset now) =>
        !IsRevoked && (ExpiresAt is null || ExpiresAt > now);

    public void SetExpiry(DateTimeOffset? expiresAt)
    {
        ExpiresAt = expiresAt;
        Touch();
    }

    public void MarkUsed(DateTimeOffset at)
    {
        LastUsedAt = at;
        Touch();
    }

    public void Revoke()
    {
        IsRevoked = true;
        Touch();
    }

    public void Rotate(byte[] cipherText, string lastFour)
    {
        ArgumentNullException.ThrowIfNull(cipherText);
        CipherText = cipherText;
        LastFour = lastFour;
        IsRevoked = false;
        Touch();
    }
}
