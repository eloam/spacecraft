using SpaceCraft.Domain.Common;

namespace SpaceCraft.Api.Features.Organizations.Requests;

public sealed record CreateOrganizationRequest(string Name, string? Slug);
