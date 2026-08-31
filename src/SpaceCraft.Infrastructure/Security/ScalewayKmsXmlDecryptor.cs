using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace SpaceCraft.Infrastructure.Security;

/// <summary>
/// Unwraps a key written by <see cref="ScalewayKmsXmlEncryptor"/>.
/// </summary>
/// <remarks>
/// Constructed by <c>Activator</c> from the type name recorded in the ring, not resolved from
/// the container, so the only shapes available are a parameterless constructor or this one.
/// The provider handed in is the <em>root</em> provider: resolving anything scoped from it
/// throws under scope validation, which is on by default in Development. Everything pulled
/// here is a singleton.
/// <para>
/// Two consequences follow from that reconstruction. The type's full name is part of the
/// stored data — moving or renaming this class orphans every key already written. And a
/// throw from <see cref="Decrypt"/> does not stop the application: Data Protection retries,
/// then drops the key from the ring and mints a fresh one, leaving a running app that can no
/// longer read a single stored credential. That silence is why the KMS is proven reachable at
/// startup instead of on first use.
/// </para>
/// </remarks>
public sealed class ScalewayKmsXmlDecryptor(IServiceProvider services) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        var ciphertext = encryptedElement.Element(ScalewayKmsXmlEncryptor.ValueElementName)?.Value
            ?? throw new InvalidOperationException(
                "This key ring entry carries no ciphertext; it was not written by this encryptor.");

        var client = services.GetRequiredService<ScalewayKmsClient>();
        var plaintext = client.DecryptAsync(ciphertext).GetAwaiter().GetResult();

        return XElement.Parse(Encoding.UTF8.GetString(plaintext));
    }
}
