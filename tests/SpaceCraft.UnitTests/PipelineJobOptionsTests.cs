using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SpaceCraft.Api.Features.Runs;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The settings that let the API start a job, checked when the host starts.
/// </summary>
/// <remarks>
/// A deployment missing one of these looks healthy from every angle — the API serves, runs
/// queue, the page says Queued — and the only symptom is that nothing ever happens. So these
/// pin that the host refuses to start, and that it names what is missing.
/// <para>
/// The class carried [Required] on all four settings for some time while being bound with a
/// plain Configure&lt;&gt;, which evaluates no annotation; a hand-written check did the work
/// instead, one key at a time. These tests are the other half of the annotations.
/// </para>
/// </remarks>
public class PipelineJobOptionsTests
{
    private static async Task<Exception?> StartWith(
        Dictionary<string, string?> settings, bool isDevelopment)
    {
        var builder = Host.CreateApplicationBuilder();

        // Hermetic, for the same reason as OptionsValidationTests: the API's appsettings.json
        // sits beside the test binary and would otherwise decide what these tests see.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSpaceCraftJobs(builder.Configuration, isDevelopment);

        // The dispatcher is a hosted service that sweeps a database this host does not have.
        // What is under test is whether the host may start, not what it does afterwards.
        foreach (var sweep in builder.Services.Where(s => s.ImplementationType == typeof(RunDispatcher)).ToList())
        {
            builder.Services.Remove(sweep);
        }

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
        ["PipelineJobs:BaseUrl"] = "https://api.scaleway.com",
        ["PipelineJobs:Region"] = "fr-par",
        ["PipelineJobs:JobDefinitionId"] = "spacecraft-pipeline",
        ["PipelineJobs:SecretKey"] = "key",
    };

    [Fact]
    public async Task A_complete_configuration_starts() =>
        (await StartWith(Valid(), isDevelopment: false)).ShouldBeNull();

    [Fact]
    public async Task A_blank_job_definition_id_stops_the_host_and_names_the_key()
    {
        var settings = Valid();
        settings["PipelineJobs:JobDefinitionId"] = "";

        var error = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("JobDefinitionId");
    }

    [Fact]
    public async Task Every_missing_key_is_reported_in_one_exception()
    {
        // The property the hand-written check did not have: it threw on the first gap, so a
        // deployment missing two keys of this section learned about them one restart at a
        // time. Within a section every failing member is reported together now. Across
        // sections the first failing one still stops startup — that is how the runtime
        // behaves, and it is not claimed otherwise anywhere.
        var settings = Valid();
        settings["PipelineJobs:JobDefinitionId"] = "";
        settings["PipelineJobs:SecretKey"] = "";

        var error = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("JobDefinitionId");
        error.Message.ShouldContain("SecretKey");
    }

    [Fact]
    public async Task A_base_url_that_is_not_a_url_is_refused()
    {
        var settings = Valid();
        settings["PipelineJobs:BaseUrl"] = "api.scaleway.com";

        var error = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("absolute");
    }

    [Fact]
    public async Task Plain_http_is_refused_outside_Development()
    {
        // The development fake speaks this API over plain HTTP and starts containers on the
        // host it runs on — which is exactly what must never be reachable from a deployment.
        var settings = Valid();
        settings["PipelineJobs:BaseUrl"] = "http://localhost:8098";

        var error = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("https");
    }

    [Fact]
    public async Task Plain_http_is_the_development_fake_and_is_allowed_there()
    {
        var settings = Valid();
        settings["PipelineJobs:BaseUrl"] = "http://localhost:8098";

        (await StartWith(settings, isDevelopment: true)).ShouldBeNull();
    }

    /// <summary>
    /// The dispatcher used to clamp these at the first read, so a sweep of 0 seconds ran every
    /// second and a batch of 1000 claimed 100, and nothing said so. A value outside its bound
    /// is a configuration error and is refused with the property named.
    /// </summary>
    [Theory]
    [InlineData("SweepSeconds", "0")]
    [InlineData("BatchSize", "101")]
    [InlineData("RedispatchAfterSeconds", "5")]
    public async Task A_knob_outside_its_range_is_refused_rather_than_clamped(string property, string value)
    {
        var settings = Valid();
        settings[$"PipelineJobs:{property}"] = value;

        var error = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain(property);
    }
}
