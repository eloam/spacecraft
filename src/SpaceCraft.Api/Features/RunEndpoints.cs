using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features;

public sealed record CreateRunRequest(Guid SpecVersionId, Guid SdkTargetId, bool Publish);

public sealed record ArtifactDownloadResponse(string Url, int ExpiresInSeconds);

public sealed record StepResponse(
    StepKind Kind,
    StepStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    double? DurationSeconds,
    string? FailureReason,
    bool HasLog);

public sealed record RunResponse(
    Guid Id,
    Guid ApiProjectId,
    Guid SpecVersionId,
    Guid SdkTargetId,
    RunStatus Status,
    RunTrigger Trigger,
    bool PublishRequested,
    string? ResolvedVersion,
    string? FailureReason,
    int AttemptCount,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    bool HasArtifact,
    IReadOnlyList<StepResponse> Steps);

public static class RunEndpoints
{
    public static RouteGroupBuilder MapRunEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations/{organizationId:guid}/runs")
            .WithTags("Runs")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .Produces<List<RunResponse>>()
            .WithSummary("Lists recent pipeline runs.");

        group.MapPost("/", CreateAsync)
            .Produces<RunResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Queues a pipeline run.");

        group.MapGet("/{runId:guid}", GetAsync)
            .Produces<RunResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns one run with its steps.");

        group.MapPost("/{runId:guid}/cancel", CancelAsync)
            .Produces<RunResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Cancels a queued or running pipeline run.");

        group.MapGet("/{runId:guid}/steps/{step}/log", GetStepLogAsync)
            .Produces<string>(StatusCodes.Status200OK, "text/plain")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns a step's captured log.");

        group.MapGet("/{runId:guid}/artifact", GetArtifactAsync)
            .Produces<ArtifactDownloadResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns a time-limited artifact download URL.");

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
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct))
        {
            return ApiResults.NotFoundOrForbidden();
        }

        var runs = await db.PipelineRuns
            .AsNoTracking()
            .Where(r => r.OrganizationId == organizationId
                        && (projectId == null || r.ApiProjectId == projectId))
            .OrderByDescending(r => r.QueuedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .Include(r => r.Steps)
            .ToListAsync(ct);

        return TypedResults.Ok(runs.Select(ToResponse).ToList());
    }

    private static async Task<IResult> CreateAsync(
        Guid organizationId,
        [FromBody] CreateRunRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        // Publishing is irreversible on a public registry, so it needs a higher role than
        // simply generating and building.
        var required = request.Publish ? OrganizationRole.Admin : OrganizationRole.Member;
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), required, ct))
        {
            return ApiResults.NotFoundOrForbidden();
        }

        var spec = await db.SpecVersions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SpecVersionId, ct);

        if (spec is null)
        {
            return ApiResults.NotFound("The spec version");
        }

        var target = await db.SdkTargets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.SdkTargetId, ct);

        if (target is null || target.ApiProjectId != spec.ApiProjectId)
        {
            return ApiResults.NotFound("The SDK target");
        }

        var project = await db.ApiProjects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == spec.ApiProjectId && p.OrganizationId == organizationId, ct);

        if (project is null)
        {
            return ApiResults.NotFoundOrForbidden();
        }

        if (!spec.IsGeneratable)
        {
            return ApiResults.Conflict("This spec version failed validation and cannot be generated from.");
        }

        // Refuse to queue a second run for the same spec and target while one is in flight.
        var alreadyQueued = await db.PipelineRuns.AnyAsync(
            r => r.SpecVersionId == spec.Id
                 && r.SdkTargetId == target.Id
                 && (r.Status == RunStatus.Queued || r.Status == RunStatus.Running),
            ct);

        if (alreadyQueued)
        {
            return ApiResults.Conflict("A run for this spec version and target is already in progress.");
        }

        var run = new PipelineRun(
            organizationId,
            project.Id,
            spec.Id,
            target.Id,
            RunTrigger.Manual,
            request.Publish,
            principal.Id());

        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/organizations/{organizationId}/runs/{run.Id}", ToResponse(run));
    }

    private static async Task<IResult> GetAsync(
        Guid organizationId,
        Guid runId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct))
        {
            return ApiResults.NotFoundOrForbidden();
        }

        var run = await db.PipelineRuns
            .AsNoTracking()
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == runId && r.OrganizationId == organizationId, ct);

        return run is null ? ApiResults.NotFound("The run") : TypedResults.Ok(ToResponse(run));
    }

    private static async Task<IResult> CancelAsync(
        Guid organizationId,
        Guid runId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Member, ct))
        {
            return ApiResults.NotFoundOrForbidden();
        }

        var run = await db.PipelineRuns
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == runId && r.OrganizationId == organizationId, ct);

        if (run is null)
        {
            return ApiResults.NotFound("The run");
        }

        if (run.IsTerminal)
        {
            return ApiResults.Conflict($"This run has already finished ({run.Status}).");
        }

        run.Cancel($"Cancelled by {principal.Email() ?? principal.Id().ToString()}.");
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToResponse(run));
    }

    private static async Task<IResult> GetStepLogAsync(
        Guid organizationId,
        Guid runId,
        string step,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        IObjectStorage storage,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct))
        {
            return ApiResults.NotFoundOrForbidden();
        }

        if (!Enum.TryParse<StepKind>(step, ignoreCase: true, out var kind))
        {
            return ApiResults.Invalid(nameof(step), $"Unknown step '{step}'.");
        }

        var run = await db.PipelineRuns
            .AsNoTracking()
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == runId && r.OrganizationId == organizationId, ct);

        if (run is null)
        {
            return ApiResults.NotFound("The run");
        }

        var logKey = run.Steps.FirstOrDefault(s => s.Kind == kind)?.LogStorageKey;
        if (logKey is null)
        {
            return ApiResults.NotFound("A log for this step");
        }

        try
        {
            // Logs are already redacted on their way out of the sandbox.
            var content = await storage.GetTextAsync(logKey, ct);
            return TypedResults.Text(content, "text/plain; charset=utf-8");
        }
        catch (ObjectNotFoundException)
        {
            return ApiResults.NotFound("A log for this step");
        }
    }

    private static async Task<IResult> GetArtifactAsync(
        Guid organizationId,
        Guid runId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        IObjectStorage storage,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Member, ct))
        {
            return ApiResults.NotFoundOrForbidden();
        }

        var run = await db.PipelineRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId && r.OrganizationId == organizationId, ct);

        if (run?.ArtifactStorageKey is null)
        {
            return ApiResults.NotFound("An artifact for this run");
        }

        // Hand out a short-lived presigned URL rather than streaming tens of megabytes
        // through the API process.
        var validFor = TimeSpan.FromMinutes(10);
        var url = storage.GetDownloadUrl(run.ArtifactStorageKey, validFor);
        return TypedResults.Ok(new ArtifactDownloadResponse(url, (int)validFor.TotalSeconds));
    }

    private static RunResponse ToResponse(PipelineRun run) =>
        new(run.Id,
            run.ApiProjectId,
            run.SpecVersionId,
            run.SdkTargetId,
            run.Status,
            run.Trigger,
            run.PublishRequested,
            run.ResolvedVersion,
            run.FailureReason,
            run.AttemptCount,
            run.QueuedAt,
            run.StartedAt,
            run.CompletedAt,
            run.ArtifactStorageKey is not null,
            run.OrderedSteps().Select(s => new StepResponse(
                s.Kind,
                s.Status,
                s.StartedAt,
                s.CompletedAt,
                s.Duration?.TotalSeconds,
                s.FailureReason,
                s.LogStorageKey is not null)).ToList());
}
