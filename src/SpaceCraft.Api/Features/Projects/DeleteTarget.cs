using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Api.Features.Deletion;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Projects;

public static class DeleteTarget
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid projectId,
        Guid targetId,
        [FromBody] ConfirmByNameRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        ResourceDeleter deleter,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        // The project has to belong to the organization this call authorised against, not
        // merely to exist. Without the second predicate the guard checked one organization
        // and the delete acted on another: anyone could create an organization of their own,
        // become its Admin by doing so, and pass another tenant's project and target ids —
        // which a Viewer of that tenant can simply read. The refusal even echoed the package
        // name back, handing over the confirmation it was asking for.
        var target = await db.SdkTargets
            .AsNoTracking()
            .Where(t => t.Id == targetId && t.ApiProjectId == projectId)
            .Where(t => db.ApiProjects.Any(p => p.Id == projectId && p.OrganizationId == organizationId))
            .FirstOrDefaultAsync(ct);

        if (target is null)
        {
            return ApiResults.NotFound("The SDK target");
        }

        if (!string.Equals(request.Name?.Trim(), target.PackageName, StringComparison.Ordinal))
        {
            return ApiResults.Invalid(
                nameof(request.Name), $"Type the package name exactly: {target.PackageName}");
        }

        if (await deleter.DeleteTargetAsync(targetId, ct) is { } refusal)
        {
            return ApiResults.Conflict(refusal.Reason);
        }

        return TypedResults.NoContent();
    }
}
