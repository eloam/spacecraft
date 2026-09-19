using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Projects.Requests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Projects;

public static class DeleteProject
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid projectId,
        [FromBody] ConfirmByNameRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        ProjectDeleter deleter,
        CancellationToken ct)
    {
        // Admin: destroying a project's history is not the same kind of act as uploading a
        // spec, which any member may do.
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        var project = await db.ApiProjects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OrganizationId == organizationId, ct);

        if (project is null)
        {
            return ApiResults.NotFound("The project");
        }

        // Checked server-side as well as in the dialog: a confirmation the API does not
        // enforce is decoration, and anything scripted against this endpoint skips the UI.
        if (!string.Equals(request.Name?.Trim(), project.Name, StringComparison.Ordinal))
        {
            return ApiResults.Invalid(nameof(request.Name), $"Type the project's name exactly: {project.Name}");
        }

        if (await deleter.DeleteAsync(projectId, ct) is { } refusal)
        {
            return ApiResults.Conflict(refusal.Reason);
        }

        return TypedResults.NoContent();
    }
}
