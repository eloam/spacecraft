using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpaceCraft.Infrastructure.Security;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The wire contract, pinned against Scaleway's published OpenAPI document.
/// </summary>
/// <remarks>
/// These assertions are the reason the development fake is worth having: they say what the
/// real API expects, and the fake is built to the same statements. A change that satisfies
/// one and not the other fails here.
/// </remarks>
public class ScalewayKmsTests
{
    private const string KeyId = KeyManagerRecorder.KeyId;

    private static (ScalewayKmsClient Client, KeyManagerRecorder Recorder) Build()
    {
        var recorder = new KeyManagerRecorder();
        var http = new HttpClient(recorder) { BaseAddress = new Uri("https://api.scaleway.com/") };
        http.DefaultRequestHeaders.Add("X-Auth-Token", "secret");

        var options = Options.Create(new KeyProtectionOptions
        {
            Provider = KeyProtectionProvider.ScalewayKms,
            Region = "fr-par", KeyId = KeyId, SecretKey = "secret",
        });

        return (new ScalewayKmsClient(http, options), recorder);
    }

    [Fact]
    public async Task Encrypt_posts_to_the_documented_route_with_the_key_in_the_path()
    {
        var (client, recorder) = Build();

        await client.EncryptAsync("hello"u8.ToArray(), TestContext.Current.CancellationToken);

        // The key id is a path segment, not a body field — Scaleway's own quickstart snippet
        // puts it in the body, and their contract does not.
        recorder.LastRequest!.RequestUri!.AbsolutePath
            .ShouldBe($"/key-manager/v1alpha1/regions/fr-par/keys/{KeyId}/encrypt");
        recorder.LastRequest.Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public async Task The_payload_travels_base64_encoded()
    {
        var (client, recorder) = Build();

        await client.EncryptAsync("hello"u8.ToArray(), TestContext.Current.CancellationToken);

        JsonDocument.Parse(recorder.LastBody!).RootElement.GetProperty("plaintext").GetString()
            .ShouldBe(Convert.ToBase64String("hello"u8.ToArray()));
    }

    [Fact]
    public async Task The_token_is_sent_bare_in_X_Auth_Token_with_no_scheme()
    {
        var (client, recorder) = Build();

        await client.EncryptAsync("hello"u8.ToArray(), TestContext.Current.CancellationToken);

        // Not Authorization, and no "Bearer" prefix: Scaleway accepts neither.
        recorder.LastRequest!.Headers.GetValues("X-Auth-Token").ShouldHaveSingleItem().ShouldBe("secret");
        recorder.LastRequest.Headers.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task A_payload_beyond_what_the_api_accepts_is_refused_before_the_call()
    {
        var (client, recorder) = Build();

        // Key Manager encrypts 1 to 65535 bytes. Caught here so the failure names the cause
        // rather than arriving as a 400 from the far end.
        await Should.ThrowAsync<InvalidOperationException>(
            client.EncryptAsync(new byte[65536], TestContext.Current.CancellationToken));

        recorder.LastRequest.ShouldBeNull();
    }

    [Fact]
    public void A_wrapped_key_names_its_decryptor_and_keeps_no_plaintext()
    {
        var (client, _) = Build();
        var encryptor = new ScalewayKmsXmlEncryptor(client, Options.Create(new KeyProtectionOptions
        {
            Region = "fr-par", KeyId = KeyId,
        }));

        var result = encryptor.Encrypt(XElement.Parse("<masterKey>c2VjcmV0</masterKey>"));

        // The type name is written into the ring and reconstructed by Activator later, so
        // moving or renaming the decryptor orphans every key already stored.
        result.DecryptorType.ShouldBe(typeof(ScalewayKmsXmlDecryptor));
        result.EncryptedElement.ToString().ShouldNotContain("c2VjcmV0");
        result.EncryptedElement.Element("keyId")!.Value.ShouldBe(KeyId);
    }

    [Fact]
    public void What_the_encryptor_writes_is_what_the_decryptor_reads()
    {
        var (client, _) = Build();
        var options = Options.Create(new KeyProtectionOptions
        {
            Region = "fr-par", KeyId = KeyId,
        });

        var encrypted = new ScalewayKmsXmlEncryptor(client, options)
            .Encrypt(XElement.Parse("<masterKey>c2VjcmV0</masterKey>"));

        var services = new ServiceCollection();
        services.AddSingleton(client);

        var decrypted = new ScalewayKmsXmlDecryptor(services.BuildServiceProvider())
            .Decrypt(encrypted.EncryptedElement);

        decrypted.ToString(SaveOptions.DisableFormatting).ShouldBe("<masterKey>c2VjcmV0</masterKey>");
    }
}
