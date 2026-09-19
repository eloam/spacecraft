using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Infrastructure.Security;

/// <summary>
/// Scaleway Key Manager over HTTP.
/// </summary>
/// <remarks>
/// Raw HTTP rather than an SDK because Scaleway publishes none for .NET — only Go, JS and
/// Python. The two calls used here are small and stable enough that a hand-written client is
/// less weight than a dependency.
/// <para>
/// The shape below is taken from Scaleway's own OpenAPI document, not from a sample:
/// <c>POST /key-manager/v1alpha1/regions/{region}/keys/{key_id}/encrypt</c> with
/// <c>{"plaintext": base64}</c> answering <c>{"key_id", "ciphertext"}</c>, and the mirror
/// call for decrypt. Region and key id are path segments — Scaleway's own quickstart snippet
/// puts the key id in the body, which the contract does not.
/// </para>
/// </remarks>
public sealed class ScalewayKmsClient(HttpClient http, IOptions<KeyProtectionOptions> options)
{
    /// <summary>The API version is part of the path. GA since 2025 despite the name.</summary>
    private const string ApiPrefix = "key-manager/v1alpha1";

    /// <summary>
    /// Encrypt accepts 1–65535 bytes. A key ring element is a single &lt;masterKey&gt; of
    /// roughly 270 bytes, so this is checked to turn a future change into a clear error
    /// rather than a rejection from the far end.
    /// </summary>
    private const int MaxPlaintextBytes = 65535;

    private KeyProtectionOptions Kms => options.Value;

    public async Task<string> EncryptAsync(byte[] plaintext, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        if (plaintext.Length is 0 or > MaxPlaintextBytes)
        {
            throw new InvalidOperationException(
                $"Key Manager encrypts between 1 and {MaxPlaintextBytes} bytes; this was {plaintext.Length}.");
        }

        var response = await PostAsync("encrypt", new { plaintext = Convert.ToBase64String(plaintext) }, ct);

        return response.Ciphertext
            ?? throw new InvalidOperationException("Key Manager returned no ciphertext.");
    }

    public async Task<byte[]> DecryptAsync(string ciphertext, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);

        var response = await PostAsync("decrypt", new { ciphertext }, ct);

        // The response also carries an optional `ciphertext`, re-encrypted under the latest
        // rotation. Ignored: the ring is written once and Data Protection would not store an
        // updated value if we produced one.
        return Convert.FromBase64String(
            response.Plaintext ?? throw new InvalidOperationException("Key Manager returned no plaintext."));
    }

    private async Task<KmsResponse> PostAsync(string verb, object body, CancellationToken ct)
    {
        var keyId = Kms.KeyId
            ?? throw new InvalidOperationException("KeyProtection:KeyId is not configured.");

        var route = $"{ApiPrefix}/regions/{Kms.Region}/keys/{keyId}/{verb}";
        using var response = await http.PostAsJsonAsync(route, body, ct);

        if (!response.IsSuccessStatusCode)
        {
            // The body carries Scaleway's own message, which names the key or the permission
            // at fault — worth far more than the status on its own.
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Key Manager refused to {verb}: {(int)response.StatusCode} {response.ReasonPhrase}. {detail}");
        }

        return await response.Content.ReadFromJsonAsync<KmsResponse>(ct)
            ?? throw new InvalidOperationException($"Key Manager returned an empty body for {verb}.");
    }

    /// <summary>Both calls answer with a subset of these, so one shape reads either.</summary>
    private sealed record KmsResponse(
        [property: JsonPropertyName("key_id")] string? KeyId,
        [property: JsonPropertyName("ciphertext")] string? Ciphertext,
        [property: JsonPropertyName("plaintext")] string? Plaintext);
}
