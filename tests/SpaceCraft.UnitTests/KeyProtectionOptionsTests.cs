using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SpaceCraft.Infrastructure.Security;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The settings that name the key manager, checked when the host starts.
/// </summary>
/// <remarks>
/// A wrong value here does not fail loudly on its own. Data Protection retries a decryptor
/// that throws, drops the key, and mints a fresh one — a process that serves normally while
/// every stored registry credential has become unreadable. So the four settings are refused
/// before any of that can run, and these pin that each refusal names the key.
/// <para>
/// They used to be a hand-written check at registration, one key at a time, and the client
/// was built from the copy that check had read rather than from the validated options. These
/// are the other half of the rules the class now states itself.
/// </para>
/// </remarks>
public class KeyProtectionOptionsTests
{
    private const string KeyId = "00000000-0000-4000-8000-000000000001";

    private static async Task<Exception?> StartWith(
        Dictionary<string, string?> settings, bool isDevelopment)
    {
        var builder = Host.CreateApplicationBuilder();

        // Hermetic, for the same reason as OptionsValidationTests: the API's appsettings.json
        // sits beside the test binary and would otherwise decide what these tests see.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);

        try
        {
            // Inside the try: with no provider outside Development the registration itself
            // refuses, before there is a host to build.
            builder.Services.AddSpaceCraftDataProtection(builder.Configuration, isDevelopment);

            // The readiness check is a hosted service that encrypts and decrypts against a
            // key manager this host does not have; it has tests of its own. What is under
            // test is whether the host may start, not what it does afterwards.
            foreach (var readiness in builder.Services.Where(s => s.ImplementationType == typeof(KeyProtectionReadiness)).ToList())
            {
                builder.Services.Remove(readiness);
            }

            using var host = builder.Build();
            await host.StartAsync(TestContext.Current.CancellationToken);
            await host.StopAsync(TestContext.Current.CancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static Dictionary<string, string?> Valid() => new()
    {
        ["KeyProtection:Provider"] = "ScalewayKms",
        ["KeyProtection:BaseUrl"] = "https://api.scaleway.com",
        ["KeyProtection:Region"] = "fr-par",
        ["KeyProtection:KeyId"] = KeyId,
        ["KeyProtection:SecretKey"] = "secret",
    };

    [Fact]
    public async Task A_complete_configuration_starts() =>
        (await StartWith(Valid(), isDevelopment: false)).ShouldBeNull();

    [Fact]
    public async Task No_provider_and_nothing_else_is_how_a_development_host_runs_unwrapped()
    {
        // What the e2e and integration hosts write on purpose: Provider=None switches the
        // wrapper off, and the four settings it would need are legitimately blank.
        var settings = new Dictionary<string, string?>
        {
            ["KeyProtection:Provider"] = "None",
            ["KeyProtection:BaseUrl"] = "",
            ["KeyProtection:Region"] = "",
            ["KeyProtection:KeyId"] = "",
            ["KeyProtection:SecretKey"] = "",
        };

        (await StartWith(settings, isDevelopment: true)).ShouldBeNull();
    }

    [Fact]
    public async Task No_provider_outside_Development_is_refused_with_the_key_named()
    {
        // Starting anyway would write the ring in clear into the database it protects.
        var settings = new Dictionary<string, string?> { ["KeyProtection:Provider"] = "None" };

        var error = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<InvalidOperationException>();
        error.Message.ShouldContain("KeyProtection:Provider");
    }

    [Fact]
    public async Task A_base_url_that_is_not_a_url_is_refused()
    {
        var settings = Valid();
        settings["KeyProtection:BaseUrl"] = "api.scaleway.com";

        var error = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("KeyProtection:BaseUrl");
        error.Message.ShouldContain("absolute");
    }

    [Fact]
    public async Task Plain_http_is_refused_outside_Development()
    {
        // The development fake speaks the same API over plain HTTP on a private network,
        // which is exactly what must never be reachable from a deployment.
        var settings = Valid();
        settings["KeyProtection:BaseUrl"] = "http://localhost:8099";

        var error = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("KeyProtection:BaseUrl");
        error.Message.ShouldContain("https");
    }

    [Fact]
    public async Task Plain_http_is_the_development_fake_and_is_allowed_there()
    {
        var settings = Valid();
        settings["KeyProtection:BaseUrl"] = "http://localhost:8099";

        (await StartWith(settings, isDevelopment: true)).ShouldBeNull();
    }

    [Fact]
    public async Task A_key_id_that_is_not_a_uuid_is_refused()
    {
        var settings = Valid();
        settings["KeyProtection:KeyId"] = "my-key";

        var error = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("KeyProtection:KeyId");
    }

    [Fact]
    public async Task A_blank_secret_key_is_refused()
    {
        var settings = Valid();
        settings["KeyProtection:SecretKey"] = "";

        var error = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("KeyProtection:SecretKey");
    }

    [Fact]
    public async Task A_blank_region_is_refused_rather_than_defaulted()
    {
        // This had a default of fr-par, which is also a region the API serves: a missing key
        // passed the known-regions check and a deployment in nl-ams would have addressed the
        // wrong one with a valid-looking name.
        var settings = Valid();
        settings["KeyProtection:Region"] = "";

        var error = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("KeyProtection:Region");
    }

    [Fact]
    public async Task A_region_the_key_manager_does_not_serve_is_refused_outside_Development()
    {
        // The fake accepts any region, so a typo would work all the way to production and
        // fail there on the first credential.
        var settings = Valid();
        settings["KeyProtection:Region"] = "fr-paris";

        var error = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("KeyProtection:Region");
        error.Message.ShouldContain("fr-par, nl-ams, pl-waw");
    }

    [Fact]
    public async Task Any_region_is_accepted_in_Development_where_the_fake_answers_to_all_of_them()
    {
        var settings = Valid();
        settings["KeyProtection:Region"] = "fr-paris";

        (await StartWith(settings, isDevelopment: true)).ShouldBeNull();
    }

    [Fact]
    public async Task Every_missing_key_is_reported_in_one_exception()
    {
        // The hand-written check threw on the first gap, so a deployment missing two keys
        // learned about them one restart at a time.
        var settings = Valid();
        settings["KeyProtection:KeyId"] = "";
        settings["KeyProtection:SecretKey"] = "";

        var error = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("KeyProtection:KeyId");
        error.Message.ShouldContain("KeyProtection:SecretKey");
    }
}
