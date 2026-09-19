using SpaceCraft.Domain.Organizations;

namespace SpaceCraft.Api.Features.Members.Requests;

public sealed record AddMemberRequest(string Email, OrganizationRole Role);
