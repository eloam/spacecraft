using System.Text.RegularExpressions;
using SpaceCraft.Domain.Registries;

namespace SpaceCraft.Domain.Projects;

/// <summary>What a registry will actually accept, and why it would not.</summary>
/// <param name="Version">The version as it will be published, or null when it was refused.</param>
/// <param name="Refusal">
/// Why the registry cannot take it, phrased for whoever writes the document. Null on success.
/// </param>
public sealed record VersionRendering(string? Version, string? Refusal)
{
    public bool Accepted => Version is not null;
}

/// <summary>
/// Renders an OpenAPI <c>info.version</c> as the registry it is bound for would accept it.
/// </summary>
/// <remarks>
/// Registries do not agree, and applying one's rules to the others is wrong in both
/// directions: <c>1.2.3.4</c> is an ordinary NuGet version and a legal Maven one, while npm
/// refuses it outright; <c>1.0</c> must be completed for npm and must not be for Maven, where
/// <c>1.0</c> and <c>1.0.0</c> are different artifacts. Rendering therefore belongs beside
/// <see cref="PackageCoordinates"/>, keyed the same way — by registry, in the domain, so no
/// call site re-derives it.
/// <para>
/// One rule governs every arm: <b>complete, never truncate</b>. Padding a missing component
/// adds no meaning — <c>1.0</c> and <c>1.0.0</c> are the same release. Dropping one removes
/// meaning, and would publish a version nobody wrote. Where a value cannot be rendered
/// without losing something, it is refused and the refusal says what to write instead.
/// </para>
/// </remarks>
public static partial class RegistryVersion
{
    /// <summary>Numeric components, then anything a registry treats as a qualifier.</summary>
    [GeneratedRegex(@"^v?(?<numbers>\d+(?:\.\d+)*)(?<suffix>[-+.].*)?$")]
    private static partial Regex Loose();

    /// <summary>Calendar versions such as 2024-01-15 or 2024.01.15.</summary>
    [GeneratedRegex(@"^v?\d{4}[-.]\d{1,2}[-.]\d{1,2}$")]
    private static partial Regex CalendarDate();

    public static VersionRendering Render(RegistryKind registry, string? declared)
    {
        if (string.IsNullOrWhiteSpace(declared))
        {
            return new VersionRendering(null, "info.version is empty, so the SDK has no version to publish.");
        }

        var trimmed = declared.Trim();

        // Refused everywhere, and not because a registry says so. A date normalises to
        // something like "2024.0.0-01-15" — a *prerelease*, which `npm install` skips by
        // default — or, where it is passed through, sorts as a number in the millions that
        // nothing after it can exceed. Publishing a version nobody can install by default,
        // or that no later release can follow, is worse than refusing it.
        if (CalendarDate().IsMatch(trimmed))
        {
            return new VersionRendering(
                null,
                $"info.version '{trimmed}' is a date. Dates cannot be ordered against releases "
                + "the way registries need — set a version like 1.0.0.");
        }

        var match = Loose().Match(trimmed);
        if (!match.Success)
        {
            return new VersionRendering(
                null,
                $"info.version '{trimmed}' carries no version number — set one like 1.0.0.");
        }

        var numbers = match.Groups["numbers"].Value.Split('.');
        var suffix = match.Groups["suffix"].Success ? match.Groups["suffix"].Value : string.Empty;

        return registry switch
        {
            // Semver: exactly three components, and anything beyond must be a -prerelease or
            // +build suffix. Completing is safe; a fourth number is not expressible.
            RegistryKind.Npm or RegistryKind.Packagist => Semver(registry, trimmed, numbers, suffix),

            // Two to four components, so nothing needs completing and nothing is lost.
            // Build metadata is accepted by the syntax and dropped by nuget.org, so it is
            // dropped here too — recording a version the registry will not hold is the one
            // outcome worth avoiding.
            RegistryKind.NuGet => numbers.Length > 4
                ? new VersionRendering(
                    null,
                    $"info.version '{trimmed}' has {numbers.Length} components; NuGet takes at most four.")
                : new VersionRendering(
                    string.Join('.', numbers) + StripBuildMetadata(suffix),
                    null),

            // Maven accepts very nearly anything and reads what it cannot parse as a
            // qualifier, so it refuses almost nothing. Passed through verbatim: completing
            // 1.0 to 1.0.0 would name a different artifact, and -SNAPSHOT belongs to a
            // different repository than the release it resembles.
            RegistryKind.Maven => new VersionRendering(trimmed, null),

            // PEP 440. A local version identifier (+something) is refused at upload, and
            // -SNAPSHOT has no equivalent that does not invent a meaning the document never
            // carried, so both are refused rather than translated.
            RegistryKind.PyPI => PyPI(trimmed, numbers, suffix),

            _ => new VersionRendering(null, $"No version rules are defined for {registry}."),
        };
    }

    private static VersionRendering Semver(RegistryKind registry, string declared, string[] numbers, string suffix)
    {
        if (numbers.Length > 3)
        {
            return new VersionRendering(
                null,
                $"info.version '{declared}' has {numbers.Length} components; {Name(registry)} takes "
                + "three — set a version like 1.0.0, or publish it to a registry that takes four.");
        }

        var major = numbers[0];
        var minor = numbers.Length > 1 ? numbers[1] : "0";
        var patch = numbers.Length > 2 ? numbers[2] : "0";

        // A dot-led qualifier is not semver: "1.0.RELEASE" would become "1.0.0.RELEASE".
        if (suffix.StartsWith('.'))
        {
            return new VersionRendering(
                null,
                $"info.version '{declared}' ends in '{suffix}', which {Name(registry)} cannot read. "
                + "Use a prerelease suffix like 1.0.0-rc.1.");
        }

        return new VersionRendering($"{major}.{minor}.{patch}{suffix}", null);
    }

    private static VersionRendering PyPI(string declared, string[] numbers, string suffix)
    {
        if (suffix.Contains('+', StringComparison.Ordinal))
        {
            return new VersionRendering(
                null,
                $"info.version '{declared}' carries a local version identifier, which PyPI refuses "
                + "at upload. Remove everything from the '+'.");
        }

        if (suffix.Contains("SNAPSHOT", StringComparison.OrdinalIgnoreCase))
        {
            return new VersionRendering(
                null,
                $"info.version '{declared}' is a Maven snapshot, which PyPI has no equivalent for. "
                + "Use a development release like 1.0.dev0.");
        }

        return new VersionRendering(string.Join('.', numbers) + suffix, null);
    }

    private static string StripBuildMetadata(string suffix)
    {
        var plus = suffix.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? suffix : suffix[..plus];
    }

    private static string Name(RegistryKind registry) =>
        registry == RegistryKind.Packagist ? "Composer" : "npm";
}
