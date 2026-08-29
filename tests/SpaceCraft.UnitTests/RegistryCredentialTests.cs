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
    public void Every_registry_kind_knows_where_it_lives() =>
        Enum.GetValues<RegistryKind>()
            .ShouldAllBe(kind => RegistryEndpoints.IsValid(RegistryEndpoints.DefaultFor(kind)));
}
