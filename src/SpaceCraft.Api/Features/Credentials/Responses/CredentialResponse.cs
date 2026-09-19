using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Api.Features.Credentials.Responses;

/// <summary>
/// Deliberately has no token field. There is no endpoint anywhere that returns a stored
/// credential: once written, it is only ever read by the publish step.
/// </summary>
/// <param name="Id">The credential's id.</param>
/// <param name="Registry">The kind of registry the token is for.</param>
/// <param name="RegistryUrl">Where it publishes.</param>
/// <param name="Label">How it is told apart from the others.</param>
/// <param name="LastFour">The token's last four characters, to recognise it by.</param>
/// <param name="IsRevoked">Whether it has been revoked.</param>
/// <param name="ExpiresAt">When the registry will stop accepting the token, if known.</param>
/// <param name="LastUsedAt">When a publish last used it.</param>
/// <param name="CreatedAt">When it was stored.</param>
/// <param name="UsedBy">
/// The SDK targets that publish with it, with the project each belongs to — what deleting
/// this credential would break, named rather than counted.
/// </param>
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
    IReadOnlyList<CredentialUsageResponse> UsedBy);
