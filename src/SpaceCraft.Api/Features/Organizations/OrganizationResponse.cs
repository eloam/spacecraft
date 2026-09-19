using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Organizations;

namespace SpaceCraft.Api.Features.Organizations;

public sealed record OrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    OrganizationRole Role,
    int ProjectCount,
    DateTimeOffset CreatedAt);
