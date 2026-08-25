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

    public static bool IsValidFor(RegistryKind registry, string? name) => registry switch
    {
        _ when string.IsNullOrWhiteSpace(name) => false,
        RegistryKind.Npm => name.Length <= 214 && NpmName().IsMatch(name),
        RegistryKind.NuGet => NuGetName().IsMatch(name),
        RegistryKind.Maven => MavenName().IsMatch(name),
        RegistryKind.PyPI => NuGetName().IsMatch(name),
        RegistryKind.Packagist => MavenName().IsMatch(name.Replace('/', ':')),
        _ => false,
    };
}
