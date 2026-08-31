using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Infrastructure.Security;

/// <summary>
/// Wraps each Data Protection key with a Scaleway Key Manager key before it is written.
/// </summary>
/// <remarks>
/// This is the half that was missing. The ring is stored in Postgres beside the credentials
/// it encrypts, so storing it in clear meant one database dump carried both the ciphertext
/// and the key that opens it. Wrapping moves the only unwrapped secret out of the database
/// and into the deployment, where a dump cannot reach it.
/// </remarks>
public sealed class ScalewayKmsXmlEncryptor(
    ScalewayKmsClient client,
    IOptions<KeyProtectionOptions> options) : IXmlEncryptor
{
    internal const string ElementName = "encryptedKey";
    internal const string ValueElementName = "value";

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        var kms = options.Value.Kms;

        // SaveOptions.DisableFormatting: the bytes are what gets authenticated, so they must
        // come back identical. Pretty-printing here would survive the round trip but adds
        // payload for nothing.
        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));

        // Synchronous by contract — IXmlEncryptor has no async form. This runs once per key
        // creation, not per protect call, so blocking here costs a single round trip at the
        // moment a key is minted.
        var ciphertext = client.EncryptAsync(plaintext).GetAwaiter().GetResult();

        var encrypted = new XElement(
            ElementName,
            new XComment(
                " Wrapped by Scaleway Key Manager. Decryptable only by a deployment holding "
                + "the API secret for the key named here — not by anyone holding this database. "),
            new XElement("region", kms.Region),
            new XElement("keyId", kms.KeyId),
            new XElement(ValueElementName, ciphertext));

        // The type name is written into the ring as `decryptorType` and reconstructed later
        // by Activator, so renaming or moving the decryptor breaks every key already stored.
        return new EncryptedXmlInfo(encrypted, typeof(ScalewayKmsXmlDecryptor));
    }
}
