using SpaceCraft.Domain.Organizations;

namespace SpaceCraft.Api.Features.Organizations.Responses;

public sealed record OrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    OrganizationRole Role,
    int ProjectCount,
    DateTimeOffset CreatedAt);
