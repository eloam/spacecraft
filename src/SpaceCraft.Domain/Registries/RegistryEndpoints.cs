using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Domain.Registries;

/// <summary>
/// Where each kind of registry lives when nobody says otherwise.
///
/// <para>
/// A default, not a constraint: the endpoint is stored on the credential precisely so an
/// organization can publish to its own registry — a Verdaccio, a Nexus, an Artifactory —
/// with the token that authenticates against it. Only the public one is guessed.
/// </para>
/// </summary>
public static class RegistryEndpoints
{
    public static string DefaultFor(RegistryKind registry) => registry switch
    {
        RegistryKind.Npm => "https://registry.npmjs.org",
        RegistryKind.NuGet => "https://api.nuget.org/v3/index.json",
        RegistryKind.Maven => "https://repo1.maven.org/maven2",
        RegistryKind.PyPI => "https://upload.pypi.org/legacy/",
        RegistryKind.Packagist => "https://packagist.org",
        _ => throw new ArgumentOutOfRangeException(nameof(registry), registry, "No default endpoint."),
    };

    /// <summary>
    /// True when this endpoint is the registry's own public one rather than something an
    /// organization runs itself. Compared on host, so a trailing slash or a scoped path does
    /// not change the answer.
    /// </summary>
    public static bool IsPublicFor(RegistryKind registry, string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var given)
        && Uri.TryCreate(DefaultFor(registry), UriKind.Absolute, out var known)
        && string.Equals(given.Host, known.Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True for an absolute http or https URL. Anything else is refused at the edge: a
    /// malformed endpoint would surface as an npm error in the middle of a publish, long
    /// after the person who typed it has moved on.
    /// </summary>
    public static bool IsValid(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
