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
        string lastFour,
        string? registryUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(cipherText);

        var endpoint = string.IsNullOrWhiteSpace(registryUrl)
            ? RegistryEndpoints.DefaultFor(registry)
                ?? throw new ArgumentException(
                    $"{registry} has no address that can be guessed; give the credential one.",
                    nameof(registryUrl))
            : registryUrl.Trim().TrimEnd('/');

        if (!RegistryEndpoints.IsValid(endpoint))
        {
            throw new ArgumentException($"'{registryUrl}' is not an absolute http or https URL.", nameof(registryUrl));
        }

        OrganizationId = organizationId;
        Registry = registry;
        Label = label;
        CipherText = cipherText;
        LastFour = lastFour;
        RegistryUrl = endpoint;
    }

    public Guid OrganizationId { get; private set; }
    public RegistryKind Registry { get; private set; }

    /// <summary>Human label, e.g. "npm automation token (acme org)".</summary>
    public string Label { get; private set; } = null!;

    /// <summary>Encrypted token bytes. Never returned over the API.</summary>
    public byte[] CipherText { get; private set; } = null!;

    /// <summary>Last four characters of the token, so operators can tell two tokens apart.</summary>
    public string LastFour { get; private set; } = null!;

    /// <summary>
    /// Where this token publishes. On the credential rather than in configuration because it
    /// is the client's decision, not the deployment's: two organizations can push the same
    /// kind of package to different registries, and a token only authenticates against the
    /// one it was issued for. Not a secret — the token is.
    /// </summary>
    public string RegistryUrl { get; private set; } = null!;

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

    /// <summary>
    /// Undoes a revocation. The token itself was never destroyed — revoking only stopped this
    /// application from using it — so a revocation made by mistake is reversible here. What it
    /// cannot undo is a token revoked at the registry, which stays useless whatever this says.
    /// </summary>
    public void Restore()
    {
        IsRevoked = false;
        Touch();
    }

    /// <summary>
    /// Replaces the stored ciphertext with the same secret, encrypted again.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="Rotate"/>. That one takes a token the operator has just
    /// replaced at the registry, which is why it clears the revocation — a new token is a
    /// working credential again. Here the secret is unchanged and only the key that wraps it
    /// moved, so a credential revoked before the rewrite is still revoked after it, and the
    /// tail shown in listings still describes the same token.
    /// </remarks>
    public void Rewrap(byte[] cipherText)
    {
        ArgumentNullException.ThrowIfNull(cipherText);
        CipherText = cipherText;
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
