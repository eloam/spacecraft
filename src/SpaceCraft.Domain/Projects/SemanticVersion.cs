using System.Text.RegularExpressions;

namespace SpaceCraft.Domain.Projects;

/// <summary>
/// Normalises an OpenAPI <c>info.version</c> into something a package registry will accept.
/// <para>
/// Specs in the wild rarely carry a strict semantic version: Keycloak's Admin API declares
/// <c>"1.0"</c>, others use <c>"v2"</c> or a date. npm and NuGet both require a semantic
/// version, so the publish step would fail long after a successful build — the worst place
/// to discover it. Normalising here keeps the SDK version traceable to the API version while
/// staying publishable.
/// </para>
/// </summary>
public static partial class SemanticVersion
{
    /// <summary>major[.minor[.patch]] with optional -prerelease and +build metadata.</summary>
    [GeneratedRegex(@"^v?(?<major>\d+)(?:\.(?<minor>\d+))?(?:\.(?<patch>\d+))?(?<suffix>[-+].*)?$")]
    private static partial Regex Loose();

    /// <summary>Calendar versions such as 2024-01-15 or 2024.01.15.</summary>
    [GeneratedRegex(@"^v?\d{4}[-.]\d{1,2}[-.]\d{1,2}$")]
    private static partial Regex CalendarDate();

    /// <summary>
    /// Returns a registry-acceptable version, or null when the input carries no recognisable
    /// version number at all (a date, a git sha, a codename).
    /// </summary>
    public static string? TryNormalize(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var trimmed = version.Trim();

        // A date would otherwise normalise to "2024.0.0-01-15" — a *prerelease*, which
        // `npm install <pkg>` skips by default. Silently publishing something nobody can
        // install by default is worse than refusing the version outright.
        if (CalendarDate().IsMatch(trimmed))
        {
            return null;
        }

        var match = Loose().Match(trimmed);
        if (!match.Success)
        {
            return null;
        }

        var major = match.Groups["major"].Value;
        var minor = match.Groups["minor"].Success ? match.Groups["minor"].Value : "0";
        var patch = match.Groups["patch"].Success ? match.Groups["patch"].Value : "0";
        var suffix = match.Groups["suffix"].Success ? match.Groups["suffix"].Value : string.Empty;

        return $"{major}.{minor}.{patch}{suffix}";
    }

    /// <summary>True when the value is already a strict three-part semantic version.</summary>
    public static bool IsStrict(string? version) =>
        version is not null && TryNormalize(version) == version;
}
