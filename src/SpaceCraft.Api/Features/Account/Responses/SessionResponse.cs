namespace SpaceCraft.Api.Features.Account.Responses;

public sealed record SessionResponse(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent);
