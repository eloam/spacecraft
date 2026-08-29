using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
    private static async Task<Exception?> StartWith(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();

        // Hermetic: the default sources pull in the API's appsettings.json, which is copied
        // beside the test binary. These tests are about what a given set of settings does,
        // so inheriting a file that changes underneath them is only a way to fail later for
        // an unrelated reason.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSpaceCraftInfrastructure(builder.Configuration);

        try
        {
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
    public async Task Credentials_stay_optional_because_empty_selects_the_ambient_chain()
    {
        // A deployment on an instance with an IAM role supplies no static keys on purpose.
        var settings = Valid();
        settings["ObjectStorage:AccessKey"] = "";
        settings["ObjectStorage:SecretKey"] = "";

        (await StartWith(settings)).ShouldBeNull();
    }
}
