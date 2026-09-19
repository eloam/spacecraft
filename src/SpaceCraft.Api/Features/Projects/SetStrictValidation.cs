using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Projects;

public sealed record StrictValidationRequest(bool Enabled);

/// <summary>
/// Turns the SDK-readiness checks on or off.
///
/// It only changes what future validations conclude — findings are written once and read
/// back from the row, so existing spec versions keep what they were told until they are
/// revalidated. Saying so is the revalidate endpoint's job, not this one's.
/// </summary>
public static class SetStrictValidation
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid projectId,
        [FromBody] StrictValidationRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        // Member, like renaming: reversible, and it publishes nothing.
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Member, ct) is { } denied)
        {
            return denied;
        }

        var project = await db.ApiProjects
            .Include(p => p.Targets)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OrganizationId == organizationId, ct);

        if (project is null)
        {
            return ApiResults.NotFound("The project");
        }

        project.SetStrictValidation(request.Enabled);
        await db.SaveChangesAsync(ct);

        var specCount = await db.SpecVersions.CountAsync(s => s.ApiProjectId == projectId, ct);

        return TypedResults.Ok(ProjectResponse.From(project, specCount));
    }
}
