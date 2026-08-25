using SpaceCraft.Domain.Projects;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.0", "1.0.0")]      // Keycloak's Admin REST API declares exactly this.
    [InlineData("1", "1.0.0")]
    [InlineData("v2.1", "2.1.0")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("  1.4  ", "1.4.0")]
    [InlineData("2.0.0-beta.1", "2.0.0-beta.1")]
    [InlineData("1.0-rc1", "1.0.0-rc1")]
    [InlineData("1.2.3+build.5", "1.2.3+build.5")]
    public void Loose_versions_normalise_to_semver(string input, string expected) =>
        SemanticVersion.TryNormalize(input).ShouldBe(expected);

    [Theory]
    [InlineData("2024-01-15")]
    [InlineData("2024.01.15")]
    [InlineData("stable")]
    [InlineData("")]
    [InlineData(null)]
    public void Values_with_no_version_number_are_rejected(string? input) =>
        // Better to fail with a clear message than to invent a version and publish it.
        SemanticVersion.TryNormalize(input).ShouldBeNull();

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("1.0", false)]
    [InlineData("v1.2.3", false)]
    public void IsStrict_identifies_already_valid_versions(string input, bool expected) =>
        SemanticVersion.IsStrict(input).ShouldBe(expected);
}
