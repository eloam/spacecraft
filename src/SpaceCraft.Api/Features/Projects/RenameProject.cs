using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Projects.Responses;
using SpaceCraft.Api.Features.Projects.Requests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Projects;

public static class RenameProject
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid projectId,
        [FromBody] RenameProjectRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        // Member, not Admin: renaming is reversible and does not touch anything published.
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Member, ct) is { } denied)
        {
            return denied;
        }

        var name = request.Name?.Trim();

        if (!ResourceName.IsValid(name))
        {
            return ApiResults.Invalid(nameof(request.Name), ResourceName.Rule);
        }

        var organization = await db.Organizations
            .Include(o => o.Projects)
            .ThenInclude(p => p.Targets)
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        var project = organization?.Projects.FirstOrDefault(p => p.Id == projectId);

        if (organization is null || project is null)
        {
            return ApiResults.NotFound("The project");
        }

        try
        {
            organization.RenameProject(projectId, name!);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResults.Conflict(ex.Message);
        }

        await db.SaveChangesAsync(ct);

        var specCount = await db.SpecVersions.CountAsync(s => s.ApiProjectId == projectId, ct);

        return TypedResults.Ok(ProjectResponse.From(project, specCount));
    }
}
