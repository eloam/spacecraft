using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SpaceCraft.Api.Email;
using SpaceCraft.Infrastructure;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// Configuration is checked when the host starts, not when a request first needs it.
///
/// These exist because the checks were once written and never run: ObjectStorageOptions
/// carried [Required] on its bucket name while being bound with a plain Configure&lt;&gt;,
/// which evaluates no annotation. A bad bucket name then surfaced as whatever the S3 SDK
/// threw, halfway through someone uploading a document. Annotating a class is only half the
/// job; these tests pin the other half.
/// </summary>
public class OptionsValidationTests
{
    private static Task<Exception?> StartWith(
        Dictionary<string, string?> settings, bool isDevelopment = true) =>
        StartWith(
            (services, configuration) => services.AddSpaceCraftInfrastructure(configuration, isDevelopment),
            settings);

    /// <summary>
    /// Starts a host with one registration — the same method the API or the runner calls —
    /// and returns what stopped it, if anything.
    /// </summary>
    private static async Task<Exception?> StartWith(
        Action<IServiceCollection, IConfiguration> register, Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();

        // Hermetic: the default sources pull in the API's appsettings.json, which is copied
        // beside the test binary. These tests are about what a given set of settings does,
        // so inheriting a file that changes underneath them is only a way to fail later for
        // an unrelated reason.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);

        try
        {
            // Inside the try: some registrations refuse at the call — the connection-string
            // guard, the pipeline's sandbox acknowledgement — before there is a host to start.
            register(builder.Services, builder.Configuration);

            // A section's registration brings the hosted services of its feature — dispatchers
            // that sweep a database this host does not have. What is under test is whether
            // the host may start, not what it does afterwards; the options validation is the
            // framework's and stays.
            foreach (var hosted in builder.Services
                .Where(s => s.ServiceType == typeof(IHostedService)
                    && s.ImplementationType?.Namespace?.StartsWith("SpaceCraft", StringComparison.Ordinal) == true)
                .ToList())
            {
                builder.Services.Remove(hosted);
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
        ["ConnectionStrings:Postgres"] = "Host=localhost;Database=x;Username=x;Password=x",
        ["ObjectStorage:BucketName"] = "spacecraft-dev",
        ["ObjectStorage:Region"] = "fr-par",
        ["ObjectStorage:ServiceUrl"] = "https://s3.fr-par.scw.cloud",
        ["ObjectStorage:AccessKey"] = "key",
        ["ObjectStorage:SecretKey"] = "secret",
    };

    [Fact]
    public async Task A_complete_storage_configuration_starts() =>
        (await StartWith(Valid())).ShouldBeNull();

    [Fact]
    public async Task A_missing_bucket_name_stops_the_host_before_it_serves_anything()
    {
        var settings = Valid();
        settings["ObjectStorage:BucketName"] = "";

        var error = await StartWith(settings);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("BucketName");
    }

    [Fact]
    public async Task A_blank_region_stops_the_host_too()
    {
        var settings = Valid();
        settings["ObjectStorage:Region"] = "";

        var error = await StartWith(settings);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("Region");
    }

    [Fact]
    public async Task An_absent_bucket_name_is_the_same_as_a_blank_one()
    {
        // The property used to carry a default, which is the one shape [Required] cannot see:
        // a missing key bound to "spacecraft" and passed. Absence has to fail like blankness.
        var settings = Valid();
        settings.Remove("ObjectStorage:BucketName");

        var error = await StartWith(settings);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("BucketName");
    }

    [Fact]
    public async Task A_blank_connection_string_is_refused_with_the_key_named()
    {
        // appsettings.json ships this key as "" so the file shows its shape. "" is not null,
        // and the guard was written with ??, so this message could never fire.
        var settings = Valid();
        settings["ConnectionStrings:Postgres"] = "";

        var error = await StartWith(settings);

        error.ShouldBeOfType<InvalidOperationException>();
        error.Message.ShouldContain("ConnectionStrings:Postgres");
    }

    [Fact]
    public async Task Credentials_may_be_empty_in_Development_where_an_ambient_chain_exists()
    {
        // A developer on an AWS instance with an IAM role supplies no static keys on purpose.
        var settings = Valid();
        settings["ObjectStorage:AccessKey"] = "";
        settings["ObjectStorage:SecretKey"] = "";

        (await StartWith(settings, isDevelopment: true)).ShouldBeNull();
    }

    [Fact]
    public async Task Credentials_are_required_outside_Development_where_there_is_no_ambient_chain()
    {
        // Nothing this platform deploys to has an IAM role to fall back on. A forgotten key
        // used to fail on the first S3 call, long after startup had reported healthy.
        var settings = Valid();
        settings["ObjectStorage:AccessKey"] = "";
        settings["ObjectStorage:SecretKey"] = "";

        var error = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("ObjectStorage:AccessKey");
    }

    [Fact]
    public async Task An_empty_endpoint_is_refused_outside_Development_rather_than_meaning_AWS()
    {
        // Empty selects real AWS S3, which is a meaning for Development and a trap everywhere
        // else: a deployment that forgets the endpoint would address a cloud it does not use,
        // with credentials it does not have, and find out on the first upload.
        var settings = Valid();
        settings["ObjectStorage:ServiceUrl"] = "";

        var error = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("ObjectStorage:ServiceUrl");
    }

    private static Dictionary<string, string?> UnencryptedRelay() => new()
    {
        ["Smtp:Host"] = "mailpit",
        ["Smtp:Port"] = "1025",
        ["Smtp:Security"] = "None",
        ["Smtp:Username"] = "user",
        ["Smtp:Password"] = "secret",
        ["Smtp:FromEmail"] = "noreply@example.test",
    };

    [Fact]
    public async Task An_unencrypted_relay_is_refused_outside_Development()
    {
        // The class cannot say this itself: it does not know where it is running. Anywhere
        // but a developer's machine, None sends the password and every reset link readable.
        var error = await StartWith(
            (services, configuration) => services.AddSpaceCraftEmail(configuration, isDevelopment: false),
            UnencryptedRelay());

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("Smtp:Security");
    }

    [Fact]
    public async Task An_unencrypted_relay_is_the_local_mail_catcher_and_is_allowed_in_Development() =>
        (await StartWith(
            (services, configuration) => services.AddSpaceCraftEmail(configuration, isDevelopment: true),
            UnencryptedRelay()))
            .ShouldBeNull();
}
