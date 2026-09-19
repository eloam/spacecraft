using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;

namespace SpaceCraft.Api.Features.Members;

public sealed record MemberResponse(
    Guid UserId,
    string Email,
    string? DisplayName,
    OrganizationRole Role,
    bool IsYou,
    DateTimeOffset JoinedAt);
