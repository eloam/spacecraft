using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Runs;

public static class ListRuns
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct,
        [FromQuery] Guid? projectId = null,
        [FromQuery] int limit = 50)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct) is { } denied)
        {
            return denied;
        }

        var runs = await db.PipelineRuns
            .AsNoTracking()
            .Where(r => r.OrganizationId == organizationId
                        && (projectId == null || r.ApiProjectId == projectId))
            .OrderByDescending(r => r.QueuedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .Include(r => r.Steps)
            .ToListAsync(ct);

        // One lookup for the page rather than one per run, and rather than none at all:
        // ProjectName exists so a caller can name the project without a second request, and
        // this list never filled it — the column reading it showed a dash on every row.
        var projects = await db.ApiProjects.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId)
            .ToDictionaryAsync(p => p.Id, p => new { p.Name, p.Slug }, ct);

        return TypedResults.Ok(
            runs.Select(r =>
            {
                var project = projects.GetValueOrDefault(r.ApiProjectId);
                return RunResponse.From(r, project?.Name, project?.Slug);
            }).ToList());
    }
}
