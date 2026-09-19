using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Api.Features.Credentials.Responses;

/// <summary>
/// Deliberately has no token field. There is no endpoint anywhere that returns a stored
/// credential: once written, it is only ever read by the publish step.
/// </summary>
public sealed record CredentialResponse(
    Guid Id,
    RegistryKind Registry,
    string RegistryUrl,
    string Label,
    string LastFour,
    bool IsRevoked,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt,
    /// <summary>
    /// The SDK targets that publish with it, with the project each belongs to — what deleting
    /// this credential would break, named rather than counted.
    /// </summary>
    IReadOnlyList<CredentialUsageResponse> UsedBy);
