using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Specs;

public static class DeleteSpec
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid projectId,
        Guid specVersionId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        SpecDeleter deleter,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Member, ct) is { } denied)
        {
            return denied;
        }

        var exists = await db.SpecVersions
            .AsNoTracking()
            .AnyAsync(
                s => s.Id == specVersionId
                     && s.ApiProjectId == projectId
                     && db.ApiProjects.Any(p => p.Id == projectId && p.OrganizationId == organizationId),
                ct);

        if (!exists)
        {
            return ApiResults.NotFound("The specification version");
        }

        if (await deleter.DeleteAsync(specVersionId, ct) is { } refusal)
        {
            return ApiResults.Conflict(refusal.Reason);
        }

        return TypedResults.NoContent();
    }
}
