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
    /// <summary>
    /// Where a registry accepts a publish when nobody says otherwise, or null when there is
    /// no such address to guess.
    /// </summary>
    /// <remarks>
    /// Null is a real answer for two of them, and guessing was worse than admitting it. Maven
    /// Central publishes through a staging service, not through repo1.maven.org, which only
    /// serves reads — a credential born with that address could never deploy, and the failure
    /// would arrive at the last step of a run. Packagist has no upload endpoint at all: a
    /// package is submitted as a VCS URL and pulled. Both therefore require an explicit
    /// address, which is exactly what the field on the credential is for.
    /// </remarks>
    public static string? DefaultFor(RegistryKind registry) => registry switch
    {
        RegistryKind.Npm => "https://registry.npmjs.org",
        RegistryKind.NuGet => "https://api.nuget.org/v3/index.json",
        RegistryKind.PyPI => "https://upload.pypi.org/legacy/",
        RegistryKind.Maven or RegistryKind.Packagist => null,
        _ => throw new ArgumentOutOfRangeException(nameof(registry), registry, "Unknown registry."),
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
    /// True for an address no publish should ever be pointed at.
    /// </summary>
    /// <remarks>
    /// A credential's endpoint is a URL somebody types and the runner then talks to, which
    /// makes it a request the platform will make on their behalf. Only the scheme was checked,
    /// so <c>http://169.254.169.254/…</c> — the address cloud instances serve their metadata,
    /// including credentials, from — was an acceptable registry, and the publish step's output
    /// is readable through the step-log endpoint.
    /// <para>
    /// This is a filter, not a boundary. A hostname that resolves to a private address passes
    /// it, and resolution can change between the check and the request. The boundary is egress
    /// policy where the step runs; see docs/security.md. What this stops is the direct case,
    /// which is the one somebody types.
    /// </para>
    /// </remarks>
    public static bool IsInternal(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // A single-label host is a name on an internal network — a container, a service
        // discovered by short name. Nothing published to a public registry looks like that.
        if (!uri.Host.Contains('.', StringComparison.Ordinal) && !uri.Host.Contains(':', StringComparison.Ordinal))
        {
            return true;
        }

        if (!System.Net.IPAddress.TryParse(uri.Host, out var address))
        {
            return false;
        }

        return System.Net.IPAddress.IsLoopback(address)
            || address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || IsPrivateV4(address);
    }

    private static bool IsPrivateV4(System.Net.IPAddress address)
    {
        if (address.AddressFamily is not System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var octets = address.GetAddressBytes();

        return octets[0] switch
        {
            10 => true,
            127 => true,
            // 169.254.0.0/16 — link-local, and the cloud metadata address lives here.
            169 when octets[1] == 254 => true,
            172 when octets[1] >= 16 && octets[1] <= 31 => true,
            192 when octets[1] == 168 => true,
            _ => false,
        };
    }

    /// <summary>
    /// True for an absolute http or https URL. Anything else is refused at the edge: a
    /// malformed endpoint would surface as an npm error in the middle of a publish, long
    /// after the person who typed it has moved on.
    /// </summary>
    public static bool IsValid(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
