using System.Text.RegularExpressions;

namespace SpaceCraft.Domain.Projects;

/// <summary>
/// The name a generated SDK is published under. Rules differ per registry, so validation
/// lives here rather than being re-derived at each call site.
/// </summary>
public sealed partial record PackageCoordinates(string Name)
{
    // npm: optional @scope/, lowercase, no leading dot/underscore, 214 chars max.
    [GeneratedRegex(@"^(?:@[a-z0-9][a-z0-9\-._]*/)?[a-z0-9][a-z0-9\-._]*$")]
    private static partial Regex NpmName();

    // NuGet: dot-separated identifiers, case-insensitive but stored as authored.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\-]{0,99}$")]
    private static partial Regex NuGetName();

    // Maven: groupId:artifactId
    [GeneratedRegex(@"^[a-z0-9][a-z0-9._\-]*:[a-z0-9][a-z0-9._\-]*$")]
    private static partial Regex MavenName();

    // PyPI, PEP 508: letters, digits, and runs of . _ - between them. Not NuGet's rule, which
    // this used to borrow: NuGet forbids a name to end in a separator and PyPI does too, but
    // NuGet's caps length at 100 and allows shapes PyPI normalises away under PEP 503.
    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9]|[._\-][A-Za-z0-9])*$")]
    private static partial Regex PyPIName();

    /// <summary>
    /// The longest a name may be. Every registry has one, and the column is 214 — a name past
    /// it used to reach the database and come back as a unique-constraint violation, which
    /// the endpoint reports as "already registered by another project". Somebody was told
    /// their name was taken when it was merely too long.
    /// </summary>
    private const int MaxLength = 214;

    public static bool IsValidFor(RegistryKind registry, string? name) => registry switch
    {
        _ when string.IsNullOrWhiteSpace(name) || name.Length > MaxLength => false,
        RegistryKind.Npm => NpmName().IsMatch(name),
        RegistryKind.NuGet => name.Length <= 100 && NuGetName().IsMatch(name),
        RegistryKind.Maven => MavenName().IsMatch(name),
        RegistryKind.PyPI => PyPIName().IsMatch(name),
        RegistryKind.Packagist => MavenName().IsMatch(name.Replace('/', ':')),
        _ => false,
    };
}
