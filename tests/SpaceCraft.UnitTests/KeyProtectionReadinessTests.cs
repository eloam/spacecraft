using System.Net;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpaceCraft.Infrastructure.Security;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// A key manager that cannot open the ring is a failed start, never a silent re-key.
/// </summary>
/// <remarks>
/// Data Protection does not fail on a decryptor that throws: it retries, drops the key from
/// the ring, and mints a fresh one. The process then serves normally while every registry
/// credential already stored is unreadable for good, and nothing in the logs names the
/// cause. The readiness check is what turns that into a refusal at startup, and these pin
/// that it does — for a credential the manager refuses, for a key that answers with the
/// wrong bytes, and for a stored key wrapped by a key the manager no longer has.
/// </remarks>
public class KeyProtectionReadinessTests
{
    private const string BaseUrl = "https://api.scaleway.com";

    /// <summary>What the ring holds. Data Protection's own repository is Postgres; this is a list.</summary>
    private sealed class InMemoryRepository : IXmlRepository
    {
        private readonly List<XElement> _elements = [];

        public IReadOnlyCollection<XElement> GetAllElements() => _elements;

        public void StoreElement(XElement element, string friendlyName) => _elements.Add(element);
    }

    private static (KeyProtectionReadiness Readiness, KeyManagerRecorder Recorder) Build(
        IXmlRepository? repository = null)
    {
        var recorder = new KeyManagerRecorder();
        var http = new HttpClient(recorder) { BaseAddress = new Uri(BaseUrl + "/") };

        var options = Options.Create(new KeyProtectionOptions
        {
            Provider = KeyProtectionProvider.ScalewayKms,
            BaseUrl = BaseUrl, Region = "fr-par", KeyId = KeyManagerRecorder.KeyId, SecretKey = "secret",
        });
        var client = new ScalewayKmsClient(http, options);

        // The decryptor is reconstructed by Activator from the type name in the ring and
        // pulls the client from the root provider, so that is what the check hands it.
        var services = new ServiceCollection();
        services.AddSingleton(client);

        var readiness = new KeyProtectionReadiness(
            client,
            options,
            Options.Create(new KeyManagementOptions { XmlRepository = repository }),
            services.BuildServiceProvider(),
            NullLogger<KeyProtectionReadiness>.Instance);

        return (readiness, recorder);
    }

    [Fact]
    public async Task A_key_manager_that_returns_what_it_was_given_lets_the_host_start()
    {
        var (readiness, recorder) = Build();

        await readiness.StartAsync(TestContext.Current.CancellationToken);

        // One encrypt, then one decrypt of what came back: the real calls, not a ping.
        recorder.LastRequest.ShouldNotBeNull().RequestUri!.AbsolutePath.ShouldEndWith("/decrypt");
    }

    [Fact]
    public async Task A_key_that_answers_with_different_bytes_stops_the_host()
    {
        // The manager is reachable and the credential is accepted, but what comes back is
        // not what went in — a rotation gone wrong, or a fake standing in for the real one.
        // Data Protection would read that as a corrupt key and mint a replacement.
        var (readiness, recorder) = Build();
        recorder.DecryptsTo = "something-else"u8.ToArray();

        var error = await Should.ThrowAsync<InvalidOperationException>(
            readiness.StartAsync(TestContext.Current.CancellationToken));

        error.Message.ShouldContain(BaseUrl);
        error.Message.ShouldContain(KeyManagerRecorder.KeyId);
    }

    [Fact]
    public async Task A_refused_credential_stops_the_host_and_says_which_key_manager()
    {
        var (readiness, recorder) = Build();
        recorder.Refusal = HttpStatusCode.Unauthorized;

        var error = await Should.ThrowAsync<InvalidOperationException>(
            readiness.StartAsync(TestContext.Current.CancellationToken));

        error.Message.ShouldContain(BaseUrl);
        error.Message.ShouldContain(KeyManagerRecorder.KeyId);

        // Scaleway's own message travels with it: it names the permission at fault, which
        // is worth more than the status on its own.
        var inner = error.InnerException.ShouldNotBeNull();
        inner.Message.ShouldContain("401");
        inner.Message.ShouldContain("authentication is denied");
    }

    [Fact]
    public async Task A_stored_key_the_manager_can_no_longer_open_stops_the_host()
    {
        // The probe alone would pass here: the manager answers, and encrypts and decrypts a
        // fresh payload perfectly. What it cannot do is open a key wrapped by the key it
        // used to hold — restored from a backup, rotated destructively. What comes back for
        // that one is not a key, and left to run, Data Protection would drop it and carry on.
        var repository = new InMemoryRepository();
        repository.StoreElement(
            new XElement(
                "key",
                new XElement(
                    ScalewayKmsXmlEncryptor.ElementName,
                    new XElement("keyId", "11111111-1111-4111-8111-111111111111"),
                    new XElement(ScalewayKmsXmlEncryptor.ValueElementName, Convert.ToBase64String("not-a-key"u8)))),
            "key-1");
        var (readiness, _) = Build(repository);

        var error = await Should.ThrowAsync<InvalidOperationException>(
            readiness.StartAsync(TestContext.Current.CancellationToken));

        // Both keys are named: the one that wrapped it and the one that could not open it.
        error.Message.ShouldContain("11111111-1111-4111-8111-111111111111");
        error.Message.ShouldContain(KeyManagerRecorder.KeyId);
        error.Message.ShouldContain(BaseUrl);
    }

    [Fact]
    public async Task A_stored_key_the_manager_opens_is_counted_and_the_host_starts()
    {
        // Wrapped by the real encryptor against the same echo, so what it wrote is what the
        // check has to read back.
        var http = new HttpClient(new KeyManagerRecorder()) { BaseAddress = new Uri(BaseUrl + "/") };
        var options = Options.Create(new KeyProtectionOptions { Region = "fr-par", KeyId = KeyManagerRecorder.KeyId });
        var wrapped = new ScalewayKmsXmlEncryptor(new ScalewayKmsClient(http, options), options)
            .Encrypt(XElement.Parse("<masterKey>c2VjcmV0</masterKey>"));

        var repository = new InMemoryRepository();
        repository.StoreElement(new XElement("key", wrapped.EncryptedElement), "key-1");
        var (readiness, recorder) = Build(repository);

        await readiness.StartAsync(TestContext.Current.CancellationToken);

        // The stored key was unwrapped after the probe: the last decrypt is of its ciphertext.
        recorder.LastBody.ShouldNotBeNull()
            .ShouldContain(wrapped.EncryptedElement.Element(ScalewayKmsXmlEncryptor.ValueElementName)!.Value);
    }
}
