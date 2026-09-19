using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Api.Features.Projects;

public sealed record ProjectResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Slug,
    SpecSource Source,
    string? GitHubRepository,
    bool StrictValidation,
    int SpecVersionCount,
    IReadOnlyList<TargetResponse> Targets,
    DateTimeOffset CreatedAt)
{
    public static ProjectResponse From(ApiProject project, int specVersionCount) =>
        new(project.Id,
            project.OrganizationId,
            project.Name,
            project.Slug,
            project.Source,
            project.GitHubRepository,
            project.StrictValidation,
            specVersionCount,
            project.Targets.Select(TargetResponse.From).ToList(),
            project.CreatedAt);
}
