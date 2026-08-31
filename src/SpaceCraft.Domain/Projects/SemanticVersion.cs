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

    /// <summary>
    /// Orders two versions by their normalised numbers: negative when <paramref name="left"/>
    /// comes first, zero when they are the same release, positive when it comes after. Null
    /// when either side carries no version a registry would take.
    ///
    /// Numeric, not textual: "1.10.0" follows "1.9.0" even though it sorts before it as a
    /// string, and that is precisely the comparison a release makes.
    ///
    /// Prerelease and build suffixes are ignored. Ordering them properly is a specification of
    /// its own, and nothing here needs to tell 1.0.0-rc.1 from 1.0.0-rc.2 — only whether the
    /// release moved forward.
    /// </summary>
    public static int? Compare(string? left, string? right)
    {
        if (Numbers(left) is not { } a || Numbers(right) is not { } b) return null;

        // A missing component is zero, so 1.2 and 1.2.0 are the same release and 1.2.0.1
        // comes after both.
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;

            if (x != y) return x.CompareTo(y);
        }

        return 0;
    }

    /// <summary>Every numeric component, however many a registry allows.</summary>
    /// <remarks>
    /// Not three. Reading only the first three meant a four-part version — ordinary in NuGet,
    /// legal in Maven — parsed as nothing at all, and the strict policy that refuses a version
    /// which repeats or goes backwards fell silent exactly where versions are least ordinary.
    /// </remarks>
    private static int[]? Numbers(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;

        var trimmed = version.Trim();

        // A date is not a version to order against. Left in, "2024-01-15" reads as major 2024
        // and every date in a year compares equal to every other — so a strict policy would
        // announce a repeat where two documents merely share a year, on top of the format rule
        // that already refuses dates outright. Two findings for one problem, and one of them
        // wrong.
        if (CalendarDate().IsMatch(trimmed)) return null;

        var match = AnyNumeric().Match(trimmed);
        if (!match.Success) return null;

        return [.. match.Groups["numbers"].Value
            .Split('.')
            .Select(part => int.Parse(part, System.Globalization.CultureInfo.InvariantCulture))];
    }

    [GeneratedRegex(@"^v?(?<numbers>\d+(?:\.\d+)*)(?:[-+.].*)?$")]
    private static partial Regex AnyNumeric();
}
