using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// Where a token publishes belongs to the token, not to the deployment: two organizations
/// can push the same kind of package to different registries, and a token only authenticates
/// against the one it was issued for.
/// </summary>
public class RegistryCredentialTests
{
    private static RegistryCredential Npm(string? url) =>
        new(Guid.CreateVersion7(), RegistryKind.Npm, "label", [1, 2, 3], "abcd", url);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unstated_endpoint_falls_back_to_the_public_one(string? url) =>
        Npm(url).RegistryUrl.ShouldBe("https://registry.npmjs.org");

    [Fact]
    public void A_private_registry_is_kept_as_given() =>
        Npm("http://verdaccio:4873").RegistryUrl.ShouldBe("http://verdaccio:4873");

    [Fact]
    public void A_trailing_slash_is_dropped() =>
        // npm builds ".../{package}" from it; two slashes reach a different path on some
        // registries and none at all on others.
        Npm("https://nexus.acme.test/repository/npm/").RegistryUrl
            .ShouldBe("https://nexus.acme.test/repository/npm");

    [Theory]
    [InlineData("verdaccio:4873")]
    [InlineData("ftp://registry.acme.test")]
    [InlineData("/repository/npm")]
    [InlineData("not a url")]
    public void Anything_that_is_not_an_http_url_is_refused(string url) =>
        // Refused here rather than inside npm, where it would surface after a build has
        // already succeeded and the person who typed it has moved on.
        Should.Throw<ArgumentException>(() => Npm(url));

    [Fact]
    public void A_registry_either_names_a_usable_address_or_names_none()
    {
        // Two of them have no address worth guessing. Maven Central deploys through a staging
        // service rather than the mirror it reads from, and Packagist takes a VCS URL instead
        // of an upload — a default for either was an address that could only fail at the last
        // step of a run. What is asserted is the pairing: an answer is either usable or absent.
        foreach (var kind in Enum.GetValues<RegistryKind>())
        {
            var endpoint = RegistryEndpoints.DefaultFor(kind);

            if (endpoint is not null)
            {
                RegistryEndpoints.IsValid(endpoint).ShouldBeTrue($"{kind} names '{endpoint}'");
            }
        }

        RegistryEndpoints.DefaultFor(RegistryKind.Maven).ShouldBeNull();
        RegistryEndpoints.DefaultFor(RegistryKind.Packagist).ShouldBeNull();
    }

    [Fact]
    public void A_registry_with_no_default_refuses_a_credential_that_gives_no_address() =>
        // Refused where the credential is built, so no path can create one that points
        // nowhere and discovers it at publish time.
        Should.Throw<ArgumentException>(() => new RegistryCredential(
            Guid.NewGuid(), RegistryKind.Maven, "maven", [1, 2, 3], "abcd"));
}
