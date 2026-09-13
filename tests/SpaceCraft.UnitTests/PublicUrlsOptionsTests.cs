using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SpaceCraft.Api.Common;
using SpaceCraft.Api.Extensions;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// Where the deployment says it lives, checked when the host starts.
/// </summary>
/// <remarks>
/// These were read nine times with a fallback to a domain the platform had moved away from.
/// A deployment that forgot the key would have mailed password-reset and invitation links
/// that looked right and went to the wrong site, and nothing would have said so. There is no
/// fallback now, and these pin that the absence is refused rather than guessed.
/// </remarks>
public class PublicUrlsOptionsTests
{
    private static async Task<(Exception? Error, PublicUrlsOptions? Options)> StartWith(
        Dictionary<string, string?> settings, bool isDevelopment)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSpaceCraftPublicUrls(builder.Configuration, isDevelopment);

        try
        {
            using var host = builder.Build();
            await host.StartAsync(TestContext.Current.CancellationToken);
            var options = host.Services.GetRequiredService<IOptions<PublicUrlsOptions>>().Value;
            await host.StopAsync(TestContext.Current.CancellationToken);
            return (null, options);
        }
        catch (Exception ex)
        {
            return (ex, null);
        }
    }

    private static Dictionary<string, string?> Valid() => new()
    {
        ["PublicUrls:App"] = "https://app.spacecraft.sh",
        ["PublicUrls:Api"] = "https://api.spacecraft.sh",
    };

    [Fact]
    public async Task A_complete_configuration_starts() =>
        (await StartWith(Valid(), isDevelopment: false)).Error.ShouldBeNull();

    [Fact]
    public async Task A_missing_app_url_is_refused_rather_than_guessed()
    {
        var settings = Valid();
        settings.Remove("PublicUrls:App");

        var (error, _) = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("App");
    }

    [Fact]
    public async Task A_value_that_is_not_a_url_is_refused()
    {
        var settings = Valid();
        settings["PublicUrls:App"] = "app.spacecraft.sh";

        var (error, _) = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("absolute");
    }

    [Fact]
    public async Task Plain_http_is_refused_outside_Development()
    {
        // A reset link over http is a bearer token on the wire.
        var settings = Valid();
        settings["PublicUrls:App"] = "http://app.spacecraft.sh";

        var (error, _) = await StartWith(settings, isDevelopment: false);

        error.ShouldBeOfType<OptionsValidationException>();
        error.Message.ShouldContain("https");
    }

    [Fact]
    public async Task Plain_http_is_localhost_and_is_allowed_in_Development()
    {
        var settings = Valid();
        settings["PublicUrls:App"] = "http://localhost:5173";
        settings["PublicUrls:Api"] = "http://localhost:8080";

        (await StartWith(settings, isDevelopment: true)).Error.ShouldBeNull();
    }

    [Fact]
    public async Task A_trailing_slash_is_removed_once_for_every_caller()
    {
        // Four of the five callers trimmed it themselves; the fifth did not, and a value ending
        // in "/" produced "https://api.spacecraft.sh//api/v1/waitlist/confirm". Normalised here
        // so that no caller has to remember.
        var settings = Valid();
        settings["PublicUrls:Api"] = "https://api.spacecraft.sh/";

        var (_, options) = await StartWith(settings, isDevelopment: false);

        options.ShouldNotBeNull().ApiRoot().ShouldBe("https://api.spacecraft.sh");
    }
}
