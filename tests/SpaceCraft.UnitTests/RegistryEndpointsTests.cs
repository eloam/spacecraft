using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// Which endpoint a credential points at decides how its token is judged, so this is the
/// question the credential form's "private registry" switch ultimately asks.
/// </summary>
public class RegistryEndpointsTests
{
    [Theory]
    [InlineData("https://registry.npmjs.org")]
    [InlineData("https://registry.npmjs.org/")]
    [InlineData("https://REGISTRY.NPMJS.ORG")]
    // A scoped path is still npmjs: the host is what issues the token.
    [InlineData("https://registry.npmjs.org/@acme")]
    public void The_public_npm_registry_is_recognised_however_it_is_written(string url)
    {
        RegistryEndpoints.IsPublicFor(RegistryKind.Npm, url).ShouldBeTrue();
    }

    [Theory]
    [InlineData("http://verdaccio:4873")]
    [InlineData("https://nexus.example.test/repository/npm")]
    [InlineData("https://npm.acme.internal")]
    public void A_registry_an_organization_runs_is_not_the_public_one(string url)
    {
        // The distinction the token check rests on: npmjs issues npm_ tokens, and nothing
        // else does. Verdaccio hands out a 108-character base64 string.
        RegistryEndpoints.IsPublicFor(RegistryKind.Npm, url).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("registry.npmjs.org")]
    public void Nothing_that_is_not_an_absolute_url_counts_as_public(string? url)
    {
        RegistryEndpoints.IsPublicFor(RegistryKind.Npm, url).ShouldBeFalse();
    }

    [Fact]
    public void Each_registry_is_measured_against_its_own_public_endpoint()
    {
        // npmjs is not PyPI's public registry, however public it is.
        RegistryEndpoints.IsPublicFor(RegistryKind.PyPI, "https://upload.pypi.org/legacy/").ShouldBeTrue();
        RegistryEndpoints.IsPublicFor(RegistryKind.PyPI, "https://registry.npmjs.org").ShouldBeFalse();
    }

    [Theory]
    [InlineData(RegistryKind.Npm)]
    [InlineData(RegistryKind.NuGet)]
    [InlineData(RegistryKind.Maven)]
    [InlineData(RegistryKind.PyPI)]
    [InlineData(RegistryKind.Packagist)]
    public void Every_registry_has_a_public_endpoint_that_recognises_itself(RegistryKind registry)
    {
        // Guards the pairing rather than the values: a registry added to the enum without a
        // default endpoint throws here instead of at the first credential that uses it.
        RegistryEndpoints.IsPublicFor(registry, RegistryEndpoints.DefaultFor(registry)).ShouldBeTrue();
    }
}
