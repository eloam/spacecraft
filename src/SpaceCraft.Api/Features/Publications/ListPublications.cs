using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Publications.Responses;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Publications;

public static class ListPublications
{
    public static async Task<Results<Ok<List<PublicationResponse>>, ProblemHttpResult>> HandleAsync(
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

        // A publication points at its target, and the target at its project. Joining here
        // rather than storing the project on the publication keeps one source of truth for
        // which project a package belongs to.
        var query =
            from publication in db.Publications.AsNoTracking()
            where publication.OrganizationId == organizationId
            join target in db.SdkTargets.AsNoTracking()
                on publication.SdkTargetId equals target.Id into targets
            from target in targets.DefaultIfEmpty()
            join project in db.ApiProjects.AsNoTracking()
                on target.ApiProjectId equals project.Id into projects
            from project in projects.DefaultIfEmpty()
            // The run is what knows which document produced the package; the publication only
            // knows the run.
            join run in db.PipelineRuns.AsNoTracking()
                on publication.PipelineRunId equals run.Id into runs
            from run in runs.DefaultIfEmpty()
            select new { publication, project, run };

        if (projectId is { } id)
        {
            query = query.Where(row => row.project != null && row.project.Id == id);
        }

        var rows = await query
            .OrderByDescending(row => row.publication.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

        return TypedResults.Ok(rows.Select(row => new PublicationResponse(
            row.publication.Id,
            row.publication.PipelineRunId,
            row.run == null ? null : row.run.SpecVersionId,
            row.publication.SdkTargetId,
            row.project?.Id,
            row.project?.Name,
            row.publication.Registry,
            row.publication.PackageName,
            row.publication.Version,
            row.publication.Status,
            row.publication.RegistryUrl,
            row.publication.FailureReason,
            row.publication.CreatedAt,
            row.publication.CompletedAt)).ToList());
    }
}
