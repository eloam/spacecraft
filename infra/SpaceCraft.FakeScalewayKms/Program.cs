using System.Security.Cryptography;
using System.Text.Json.Serialization;

// A stand-in for Scaleway Key Manager, the way MinIO stands in for Object Storage and Mailpit
// for an SMTP relay.
//
// It exists so the code that runs in production runs here too. SpaceCraft wraps its Data
// Protection key ring with a KMS key; without a local key manager that path would only ever
// execute in production, and a wrong header or a mis-encoded payload would be discovered
// there. The routes, field names and limits below are taken from Scaleway's published
// OpenAPI document so that what passes here is what passes there.
//
// It is a fake, not a key manager: the key sits in a file next to it, unwrapped. The
// application refuses to start against a plaintext endpoint outside Development, which is
// what keeps this from ever standing in for the real thing.

var builder = WebApplication.CreateBuilder(args);

var expectedToken = builder.Configuration["Fake:ExpectedToken"] ?? "fake-token";
var expectedKeyId = builder.Configuration["Fake:KeyId"] ?? "00000000-0000-4000-8000-000000000001";

// Persisted, and this is not a detail. The ring in Postgres is encrypted with this key: if it
// were regenerated on each start, every credential already stored would become undecryptable
// while the database still held it. Backed by a volume in compose for the same reason.
var keyPath = builder.Configuration["Fake:KeyPath"] ?? "/data/master.key";
var key = LoadOrCreateKey(keyPath);

var app = builder.Build();

// The real API answers 401 without a valid X-Auth-Token. Enforced here so a client that
// forgets the header — or sends "Bearer <token>", which Scaleway does not use — fails on a
// developer machine instead of in production.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/key-manager"))
    {
        var token = context.Request.Headers["X-Auth-Token"].ToString();
        if (!string.Equals(token, expectedToken, StringComparison.Ordinal))
        {
            await Results.Json(
                new { message = "denied authentication", type = "denied_authentication" },
                statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(context);
            return;
        }
    }

    await next(context);
});

const string KeyRoute = "/key-manager/v1alpha1/regions/{region}/keys/{keyId}";

app.MapPost($"{KeyRoute}/encrypt", (string region, string keyId, EncryptRequest request) =>
{
    if (!string.Equals(keyId, expectedKeyId, StringComparison.OrdinalIgnoreCase))
    {
        return NotFoundKey(keyId);
    }

    if (!TryDecodeBase64(request.Plaintext, out var plaintext))
    {
        return Invalid("plaintext", "must be base64");
    }

    // Scaleway's contract: 1 to 65535 bytes. Enforced so an oversized payload is refused
    // here rather than accepted locally and rejected in production.
    if (plaintext.Length is 0 or > 65535)
    {
        return Invalid("plaintext", "data size must be between 1 and 65535 bytes");
    }

    return Results.Json(new EncryptResponse(keyId, Convert.ToBase64String(Seal(key, plaintext))));
});

app.MapPost($"{KeyRoute}/decrypt", (string region, string keyId, DecryptRequest request) =>
{
    if (!string.Equals(keyId, expectedKeyId, StringComparison.OrdinalIgnoreCase))
    {
        return NotFoundKey(keyId);
    }

    if (!TryDecodeBase64(request.Ciphertext, out var ciphertext))
    {
        return Invalid("ciphertext", "must be base64");
    }

    if (ciphertext.Length is 0 or > 131071)
    {
        return Invalid("ciphertext", "data size must be between 1 and 131071 bytes");
    }

    try
    {
        // The response also carries an optional re-encrypted ciphertext after a rotation.
        // This fake never rotates, so it is omitted — as the real API omits it when the
        // payload is already under the current key.
        return Results.Json(new DecryptResponse(keyId, Convert.ToBase64String(Open(key, ciphertext))));
    }
    catch (CryptographicException)
    {
        return Invalid("ciphertext", "could not be decrypted with this key");
    }
});

// Lets compose wait for readiness the way it does for MinIO and Verdaccio.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

static byte[] LoadOrCreateKey(string path)
{
    if (File.Exists(path))
    {
        return File.ReadAllBytes(path);
    }

    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var created = RandomNumberGenerator.GetBytes(32);
    File.WriteAllBytes(path, created);
    return created;
}

// AES-GCM, matching what a symmetric Key Manager key does: nonce, tag and ciphertext travel
// together so one opaque blob goes back to the caller.
static byte[] Seal(byte[] key, byte[] plaintext)
{
    var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
    var tag = new byte[AesGcm.TagByteSizes.MaxSize];
    var ciphertext = new byte[plaintext.Length];

    using var aes = new AesGcm(key, tag.Length);
    aes.Encrypt(nonce, plaintext, ciphertext, tag);

    return [.. nonce, .. tag, .. ciphertext];
}

static byte[] Open(byte[] key, byte[] sealed_)
{
    var nonceLength = AesGcm.NonceByteSizes.MaxSize;
    var tagLength = AesGcm.TagByteSizes.MaxSize;

    if (sealed_.Length < nonceLength + tagLength)
    {
        throw new CryptographicException("Payload is too short to have been produced here.");
    }

    var nonce = sealed_.AsSpan(0, nonceLength);
    var tag = sealed_.AsSpan(nonceLength, tagLength);
    var ciphertext = sealed_.AsSpan(nonceLength + tagLength);
    var plaintext = new byte[ciphertext.Length];

    using var aes = new AesGcm(key, tagLength);
    aes.Decrypt(nonce, ciphertext, tag, plaintext);

    return plaintext;
}

static bool TryDecodeBase64(string? value, out byte[] bytes)
{
    bytes = [];
    if (string.IsNullOrEmpty(value))
    {
        return false;
    }

    var buffer = new byte[((value.Length * 3) + 3) / 4];
    if (!Convert.TryFromBase64String(value, buffer, out var written))
    {
        return false;
    }

    bytes = buffer[..written];
    return true;
}

static IResult NotFoundKey(string keyId) => Results.Json(
    new { message = $"key {keyId} is not found", type = "not_found" },
    statusCode: StatusCodes.Status404NotFound);

static IResult Invalid(string field, string reason) => Results.Json(
    new { message = "invalid argument(s)", details = new[] { new { argument_name = field, reason } } },
    statusCode: StatusCodes.Status400BadRequest);

internal sealed record EncryptRequest([property: JsonPropertyName("plaintext")] string? Plaintext);

internal sealed record DecryptRequest([property: JsonPropertyName("ciphertext")] string? Ciphertext);

internal sealed record EncryptResponse(
    [property: JsonPropertyName("key_id")] string KeyId,
    [property: JsonPropertyName("ciphertext")] string Ciphertext);

internal sealed record DecryptResponse(
    [property: JsonPropertyName("key_id")] string KeyId,
    [property: JsonPropertyName("plaintext")] string Plaintext);
