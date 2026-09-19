using Microsoft.AspNetCore.DataProtection;

namespace SpaceCraft.Infrastructure.Security;

/// <summary>
/// Backed by ASP.NET Core Data Protection.
/// <para>
/// The key ring is persisted in Postgres in every environment, so the API and the runner
/// read the same one, and is wrapped by the Key Manager outside Development —
/// <c>KeyProtectionExtensions</c> refuses to start without it. Lose the ring and every stored
/// token is unreadable; copy it unwrapped and every stored token is readable.
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
