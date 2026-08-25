using Microsoft.AspNetCore.DataProtection;

namespace SpaceCraft.Infrastructure.Security;

/// <summary>
/// Backed by ASP.NET Core Data Protection.
/// <para>
/// In development the key ring sits on disk. In production it must be persisted to a
/// shared store and wrapped by a KMS key (<c>ProtectKeysWithAzureKeyVault</c>,
/// <c>ProtectKeysWithAwsKms</c>, or equivalent) — otherwise every stored token becomes
/// unreadable the moment the key ring is lost, and readable to anyone who copies it.
/// </para>
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    // Changing this purpose string invalidates every previously stored token.
    private const string Purpose = "SpaceCraft.RegistryCredential.v1";

    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public byte[] Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        return _protector.Protect(System.Text.Encoding.UTF8.GetBytes(plaintext));
    }

    public string Unprotect(byte[] cipherText)
    {
        ArgumentNullException.ThrowIfNull(cipherText);
        return System.Text.Encoding.UTF8.GetString(_protector.Unprotect(cipherText));
    }
}
