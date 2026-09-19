using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Runs;

public static class GetRun
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid runId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct) is { } denied)
        {
            return denied;
        }

        var run = await db.PipelineRuns
            .AsNoTracking()
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == runId && r.OrganizationId == organizationId, ct);

        if (run is null)
        {
            return ApiResults.NotFound("The run");
        }

        // Looked up here rather than joined into the list query: only the detail view names
        // the project, and the list would pay for it on every row.
        var project = await db.ApiProjects
            .Where(p => p.Id == run.ApiProjectId)
            .Select(p => new { p.Name, p.Slug })
            .FirstOrDefaultAsync(ct);

        return TypedResults.Ok(RunResponse.From(run, project?.Name, project?.Slug));
    }
}
