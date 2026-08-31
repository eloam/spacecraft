using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// One declared version, rendered as each registry would take it.
/// </summary>
/// <remarks>
/// These cases are the whole argument for rendering per registry rather than once. Read the
/// rows sideways: the same input becomes different published versions, and two of them are
/// refusals rather than translations.
/// </remarks>
public class RegistryVersionTests
{
    [Theory]
    // The common case: a strict three-part version needs no translation anywhere.
    [InlineData("1.0.1", RegistryKind.Npm, "1.0.1")]
    [InlineData("1.0.1", RegistryKind.Maven, "1.0.1")]
    [InlineData("1.0.1", RegistryKind.NuGet, "1.0.1")]
    [InlineData("1.0.1", RegistryKind.PyPI, "1.0.1")]
    // Completing is safe for semver and wrong for Maven, where 1.0 and 1.0.0 are different
    // artifacts.
    [InlineData("1.0", RegistryKind.Npm, "1.0.0")]
    [InlineData("1.0", RegistryKind.Maven, "1.0")]
    [InlineData("1.0", RegistryKind.NuGet, "1.0")]
    // Four components: ordinary for NuGet, legal for Maven, inexpressible in semver.
    [InlineData("1.2.3.4", RegistryKind.NuGet, "1.2.3.4")]
    [InlineData("1.2.3.4", RegistryKind.Maven, "1.2.3.4")]
    // A snapshot means a different repository; npm has only prereleases.
    [InlineData("1.0-SNAPSHOT", RegistryKind.Maven, "1.0-SNAPSHOT")]
    [InlineData("1.0-SNAPSHOT", RegistryKind.Npm, "1.0.0-SNAPSHOT")]
    // A leading v is decoration, not a component.
    [InlineData("v2.1.0", RegistryKind.Npm, "2.1.0")]
    public void A_version_is_rendered_as_its_registry_takes_it(
        string declared, RegistryKind registry, string expected)
    {
        RegistryVersion.Render(registry, declared).Version.ShouldBe(expected);
    }

    [Fact]
    public void Npm_refuses_a_fourth_component_rather_than_dropping_it()
    {
        // The rule the whole type rests on: completing adds no meaning, truncating removes
        // it. Publishing 1.2.3 for a document that declared 1.2.3.4 would be a version
        // nobody wrote.
        var rendered = RegistryVersion.Render(RegistryKind.Npm, "1.2.3.4");

        rendered.Accepted.ShouldBeFalse();
        rendered.Refusal!.ShouldContain("4 components");
        rendered.Refusal!.ShouldContain("npm takes three");
    }

    [Fact]
    public void NuGet_drops_build_metadata_because_the_registry_would()
    {
        // Accepted by the syntax and discarded by nuget.org. Discarding it here keeps the
        // recorded version equal to the published one.
        RegistryVersion.Render(RegistryKind.NuGet, "1.0.0+build.5").Version.ShouldBe("1.0.0");
    }

    [Fact]
    public void PyPI_refuses_a_local_version_rather_than_publishing_something_else()
    {
        var rendered = RegistryVersion.Render(RegistryKind.PyPI, "1.0.0+build.5");

        rendered.Accepted.ShouldBeFalse();
        rendered.Refusal!.ShouldContain("local version identifier");
    }

    [Fact]
    public void PyPI_refuses_a_snapshot_rather_than_inventing_an_equivalent()
    {
        // .dev0 would be a plausible translation and a claim the document never made.
        RegistryVersion.Render(RegistryKind.PyPI, "1.0-SNAPSHOT").Refusal!
            .ShouldContain("no equivalent");
    }

    [Theory]
    [InlineData(RegistryKind.Npm)]
    [InlineData(RegistryKind.Maven)]
    [InlineData(RegistryKind.NuGet)]
    [InlineData(RegistryKind.PyPI)]
    public void A_date_is_refused_everywhere(RegistryKind registry)
    {
        // Not a registry rule but ours: a date orders as a number nothing later can exceed,
        // and normalising it produces a prerelease installers skip by default.
        RegistryVersion.Render(registry, "2024-01-15").Accepted.ShouldBeFalse();
    }

    [Fact]
    public void A_refusal_says_what_to_write_instead()
    {
        // The message reaches whoever authored the document, and "invalid" tells them
        // nothing they can act on.
        RegistryVersion.Render(RegistryKind.Npm, "banana").Refusal!.ShouldContain("1.0.0");
    }
}

/// <summary>
/// Ordering, which decides whether a release moved forward.
/// </summary>
public class VersionOrderingTests
{
    [Theory]
    // The gap that made the strict policy silent: a fourth component parsed as nothing, so
    // the same version published twice was never reported as a repeat.
    [InlineData("1.2.3.4", "1.2.3.4", 0)]
    [InlineData("1.2.3.5", "1.2.3.4", 1)]
    [InlineData("1.2.3.4", "1.2.3.5", -1)]
    // A missing component is zero, so these name the same release.
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("1.2.0.1", "1.2", 1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    public void Versions_order_on_every_component_they_carry(string left, string right, int expected)
    {
        Math.Sign(SemanticVersion.Compare(left, right)!.Value).ShouldBe(expected);
    }

    [Fact]
    public void Something_carrying_no_number_cannot_be_ordered()
    {
        SemanticVersion.Compare("banana", "1.0.0").ShouldBeNull();
    }
}

/// <summary>
/// Dates, which look like versions and order like nothing.
/// </summary>
public class VersionDateTests
{
    [Theory]
    [InlineData("2024-01-15", "2024-01-15")]
    [InlineData("2024-02-01", "2024-01-14")]
    [InlineData("2024.01.15", "1.0.0")]
    public void A_date_cannot_be_ordered_against_anything(string left, string right)
    {
        // Read as numbers, every date in a year compares equal to every other, and a strict
        // policy would report a repeat where two documents merely share a year — while the
        // format rule is already refusing the date outright.
        SemanticVersion.Compare(left, right).ShouldBeNull();
    }
}
