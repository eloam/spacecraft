using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features;

public sealed record PublicationResponse(
    Guid Id,
    Guid PipelineRunId,
    /// <summary>
    /// The document this package was generated from. Carried so a caller can say which spec
    /// version is live without joining publications to runs itself.
    /// </summary>
    Guid? SpecVersionId,
    Guid SdkTargetId,
    Guid? ApiProjectId,
    string? ProjectName,
    RegistryKind Registry,
    string PackageName,
    string Version,
    PublicationStatus Status,
    string? RegistryUrl,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public static class PublicationEndpoints
{
    public static RouteGroupBuilder MapPublicationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations/{organizationId:guid}/publications")
            .WithTags("Publications")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .Produces<List<PublicationResponse>>()
            .WithSummary("Lists SDK versions pushed to registries, newest first.");

        return api;
    }

    private static async Task<IResult> ListAsync(
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
