using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Api.Features.Credentials.Requests;

public sealed record CreateCredentialRequest(
    RegistryKind Registry,
    string Label,
    string Token,
    /// <summary>
    /// Half of the credential wherever a registry wants one — Maven, Nexus, Artifactory, and
    /// PyPI where it is the literal "__token__". Absent for npm and NuGet, which authenticate
    /// with the token alone.
    /// </summary>
    string? Username,
    DateTimeOffset? ExpiresAt,
    /// <summary>Where this token publishes. Empty means the public registry for its kind.</summary>
    string? RegistryUrl = null);
