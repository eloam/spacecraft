using SpaceCraft.Api.Common;

namespace SpaceCraft.Api.Features.Account;

public sealed record AccountResponse(
    Guid Id,
    string Email,
    string? DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    int OrganizationCount);
