using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SpaceCraft.Api.Extensions;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The CORS policy is derived from <c>PublicUrls:App</c> rather than configured on its own.
/// </summary>
/// <remarks>
/// A derivation can be wrong by a character — a trailing slash, a path — and the browser's
/// <c>Origin</c> header would then never match, which fails as a blank page in production and
/// nowhere else: the development and e2e front ends reach the API through a proxy and never
/// send a cross-origin request. So the policy is read back here from the same registration
/// the API uses, and compared to the origin a browser would actually send.
/// </remarks>
public class CorsPolicyTests
{
    private static async Task<CorsPolicy> PolicyFor(string appUrl)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PublicUrls:App"] = appUrl,
            ["PublicUrls:Api"] = "https://api.spacecraft.sh",
        });
        builder.Services.AddSpaceCraftPublicUrls(builder.Configuration, isDevelopment: false);
        builder.Services.AddSpaceCraftCors();

        using var host = builder.Build();
        var provider = host.Services.GetRequiredService<ICorsPolicyProvider>();
        var policy = await provider.GetPolicyAsync(new Microsoft.AspNetCore.Http.DefaultHttpContext(), ApiServiceExtensions.SpaCorsPolicy);
        return policy.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_front_end_origin_is_the_one_allowed()
    {
        var policy = await PolicyFor("https://app.spacecraft.sh");

        policy.Origins.ShouldBe(["https://app.spacecraft.sh"]);
        policy.SupportsCredentials.ShouldBeTrue();
    }

    [Fact]
    public async Task A_path_or_a_trailing_slash_on_the_url_does_not_leak_into_the_origin()
    {
        // Origins have no path. "https://app.spacecraft.sh/" would never equal what the
        // browser sends, and every request from the front end would be refused.
        var policy = await PolicyFor("https://app.spacecraft.sh/some/where/");

        policy.Origins.ShouldBe(["https://app.spacecraft.sh"]);
    }

    [Fact]
    public async Task A_port_is_part_of_the_origin_and_is_kept()
    {
        var policy = await PolicyFor("https://app.spacecraft.sh:8443");

        policy.Origins.ShouldBe(["https://app.spacecraft.sh:8443"]);
    }
}
