using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Projects.Responses;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Projects;

public static class GetProject
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid projectId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct) is { } denied)
        {
            return denied;
        }

        var project = await db.ApiProjects
            .AsNoTracking()
            .Include(p => p.Targets)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OrganizationId == organizationId, ct);

        if (project is null)
        {
            return ApiResults.NotFound("The project");
        }

        var specCount = await db.SpecVersions.CountAsync(s => s.ApiProjectId == projectId, ct);
        return TypedResults.Ok(ProjectResponse.From(project, specCount));
    }
}
