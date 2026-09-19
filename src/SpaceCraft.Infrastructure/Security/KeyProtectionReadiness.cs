using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Infrastructure.Security;

/// <summary>
/// Proves the key manager answers before Data Protection touches the ring, and stops the host
/// if it does not.
/// </summary>
/// <remarks>
/// This exists because of how Data Protection fails. An <c>IXmlDecryptor</c> that throws does
/// not bring the application down: the read is retried, the key is then dropped from the ring,
/// and — key generation being automatic — a fresh one is minted and persisted. The result is a
/// process that serves traffic normally while every registry credential already stored has
/// become permanently unreadable, with nothing in the logs that names the cause.
/// <para>
/// A misconfigured secret, an unreachable endpoint or a deleted key must therefore be a failed
/// start, not a silent re-key. One round trip through the real calls is enough to tell them
/// apart, and it costs one request per boot.
/// </para>
/// </remarks>
public sealed class KeyProtectionReadiness(
    ScalewayKmsClient client,
    IOptions<KeyProtectionOptions> options,
    IOptions<KeyManagementOptions> keyManagement,
    IServiceProvider services,
    ILogger<KeyProtectionReadiness> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var kms = options.Value;

        // Distinctive on purpose: if this ever surfaces in a log or a KMS audit trail, it
        // should be obvious that it is a probe and not a key.
        var probe = "spacecraft-key-protection-readiness"u8.ToArray();

        try
        {
            var ciphertext = await client.EncryptAsync(probe, cancellationToken);
            var roundTripped = await client.DecryptAsync(ciphertext, cancellationToken);

            // Decrypting to something else means the key answered but is not the key that
            // encrypted — a rotation gone wrong, or a fake standing in for the real one.
            if (!roundTripped.AsSpan().SequenceEqual(probe))
            {
                throw new InvalidOperationException(
                    "The key manager returned a different payload than it was given.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Key protection is configured against {kms.BaseUrl} with key {kms.KeyId}, but a "
                + "test encrypt/decrypt failed. Refusing to start: Data Protection would not "
                + "fail on this, it would silently discard the key ring and mint a new one, "
                + "making every stored registry credential unreadable.",
                ex);
        }

        var readable = UnwrapEveryStoredKey(kms);

        logger.LogInformation(
            "Key ring is wrapped by Key Manager key {KeyId} in {Region} at {BaseUrl}; "
            + "{Readable} stored key(s) unwrapped.",
            kms.KeyId, kms.Region, kms.BaseUrl, readable);
    }

    /// <summary>
    /// Unwraps every key already in the ring, and refuses to start if one cannot be read.
    /// </summary>
    /// <remarks>
    /// The round trip above is not enough on its own, and the gap is the dangerous case. A
    /// probe encrypted and decrypted now succeeds against a key manager holding a brand-new
    /// master key — restored from a backup, rotated destructively, or a fake standing in for
    /// the real one — while every key already stored was wrapped by the old one and can no
    /// longer be opened. Observed behaviour in that state: the read fails, the unreadable key
    /// is dropped, and a fresh one is minted and persisted. The process then runs normally,
    /// protecting new credentials, while every credential stored before is unreadable for
    /// good and nothing says so.
    /// <para>
    /// Reading the stored elements here, before Data Protection loads the ring, is what turns
    /// that into a failed start.
    /// </para>
    /// </remarks>
    private int UnwrapEveryStoredKey(KeyProtectionOptions kms)
    {
        var repository = keyManagement.Value.XmlRepository;
        if (repository is null)
        {
            return 0;
        }

        var decryptor = new ScalewayKmsXmlDecryptor(services);
        var wrapped = repository.GetAllElements()
            .SelectMany(element => element.Descendants(ScalewayKmsXmlEncryptor.ElementName))
            .ToList();

        foreach (var element in wrapped)
        {
            try
            {
                decryptor.Decrypt(element);
            }
            catch (Exception ex)
            {
                var keyId = element.Element("keyId")?.Value ?? "unknown";
                throw new InvalidOperationException(
                    $"A key in the ring was wrapped by Key Manager key {keyId} and can no longer "
                    + $"be unwrapped by {kms.KeyId} at {kms.BaseUrl}. Refusing to start: left to "
                    + "run, Data Protection would discard this key, mint a replacement, and carry "
                    + "on — every registry credential encrypted under it unreadable for good.",
                    ex);
            }
        }

        return wrapped.Count;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Says out loud, once per start, that the key ring is being stored unwrapped.
/// </summary>
/// <remarks>
/// Only reachable in Development — every other environment refuses to start without a
/// provider. It mirrors the announcement the email transport makes about an unencrypted
/// relay: the unsafe development shortcut is allowed, but never silent.
/// </remarks>
public sealed class UnprotectedKeyRingWarning(ILogger<UnprotectedKeyRingWarning> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "The Data Protection key ring is stored unencrypted, in the same database as the "
            + "registry credentials it protects. One dump carries both. Development only.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
