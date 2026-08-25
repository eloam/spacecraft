namespace SpaceCraft.Infrastructure.Security;

/// <summary>
/// Encrypts registry tokens at rest.
/// <para>
/// Deliberately narrow: the only thing that decrypts is the publish step, immediately
/// before handing the token to the registry CLI. Nothing else in the system ever calls
/// <see cref="Unprotect"/>, and no API response path has access to this service.
/// </para>
/// </summary>
public interface ISecretProtector
{
    byte[] Protect(string plaintext);

    string Unprotect(byte[] cipherText);
}
