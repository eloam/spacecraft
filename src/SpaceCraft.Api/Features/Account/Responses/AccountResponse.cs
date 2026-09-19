namespace SpaceCraft.Api.Features.Account.Responses;

public sealed record AccountResponse(
    Guid Id,
    string Email,
    string? DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    int OrganizationCount);
