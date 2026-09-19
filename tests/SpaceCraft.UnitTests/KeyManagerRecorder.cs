using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SpaceCraft.UnitTests;

/// <summary>
/// Answers like Key Manager, recording what it was asked.
/// </summary>
/// <remarks>
/// The cipher is an echo: encrypt hands the plaintext back as the ciphertext and decrypt
/// hands the ciphertext back as the plaintext. The point is the envelope — routes, headers,
/// base64 — not the algorithm, and an echo is the one cipher whose round trip a test can
/// read. What it answers can be bent two ways, for the checks that exist to catch a key
/// manager behaving badly: <see cref="Refusal"/> answers every call with that status, and
/// <see cref="DecryptsTo"/> makes decrypt return something other than what was encrypted.
/// </remarks>
internal sealed class KeyManagerRecorder : HttpMessageHandler
{
    public const string KeyId = "00000000-0000-4000-8000-000000000001";

    public HttpRequestMessage? LastRequest { get; private set; }

    public string? LastBody { get; private set; }

    /// <summary>When set, every call is answered with this status and Scaleway's error shape.</summary>
    public HttpStatusCode? Refusal { get; set; }

    /// <summary>
    /// When set, decrypt returns these bytes whatever the ciphertext was — a key that
    /// answers, but is not the key that encrypted.
    /// </summary>
    public byte[]? DecryptsTo { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        if (Refusal is { } status)
        {
            return new HttpResponseMessage(status)
            {
                Content = JsonContent.Create(new { message = "authentication is denied", type = "denied_authentication" }),
            };
        }

        var body = JsonDocument.Parse(LastBody!).RootElement;

        var payload = body.TryGetProperty("plaintext", out var plaintext)
            ? new { key_id = KeyId, ciphertext = plaintext.GetString() }
            : (object)new
            {
                key_id = KeyId,
                plaintext = DecryptsTo is null
                    ? body.GetProperty("ciphertext").GetString()
                    : Convert.ToBase64String(DecryptsTo),
            };

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
    }
}
