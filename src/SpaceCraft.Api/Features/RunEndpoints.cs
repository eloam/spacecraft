using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features;

public sealed record CreateRunRequest(Guid SpecVersionId, Guid SdkTargetId, bool Publish);

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
    /// <summary>Carried so a caller can name the project without a second request.</summary>
    string? ProjectName,
    Guid SpecVersionId,
    /// <summary>Which version of the document, as the project numbers them.</summary>
    int SpecSequence,
    Guid SdkTargetId,
    /// <summary>
    /// What the SDK target said when this run was queued, not what it says now. A target
    /// renamed or repointed since would otherwise rewrite the history of every run before it.
    /// </summary>
    SdkLanguage TargetLanguage,
    /// <inheritdoc cref="TargetLanguage"/>
    string TargetPackageName,
    /// <inheritdoc cref="TargetLanguage"/>
    RegistryKind TargetRegistry,
    /// <inheritdoc cref="TargetLanguage"/>
    bool TargetAutoPublish,
    /// <summary>Where its credential pointed then; null when it had none.</summary>
    string? TargetRegistryUrl,
    /// <summary>The credential's label as it read then; null when the target had none.</summary>
    string? TargetCredentialLabel,
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
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status403Forbidden);

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
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns the packaged SDK this run produced.");

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
        var projectNames = await db.ApiProjects.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId)
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        return TypedResults.Ok(
            runs.Select(r => ToResponse(r, projectNames.GetValueOrDefault(r.ApiProjectId))).ToList());
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
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), required, ct) is { } denied)
        {
            return denied;
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

        // A version the target's registry cannot take is deliberately not refused here. The
        // pipeline's first step is validation, and judging the document against the registry
        // it is bound for is what that step is for. Refusing at creation would be quieter in
        // the wrong way: no run, no step, no log, and nothing in the history for someone
        // waiting on a publish to find. A run that fails at validate says the same sentence
        // and leaves it written down.

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

        // Read now, stored on the run: the address travels with the credential, so revoking
        // or repointing one later must not change where this run says it published.
        var credential = target.RegistryCredentialId is { } credentialId
            ? await db.RegistryCredentials.AsNoTracking()
                .Where(c => c.Id == credentialId && c.OrganizationId == organizationId)
                .Select(c => new { c.RegistryUrl, c.Label })
                .FirstOrDefaultAsync(ct)
            : null;

        var run = new PipelineRun(
            organizationId,
            project.Id,
            spec.Id,
            spec.Sequence,
            target,
            credential?.RegistryUrl,
            credential?.Label,
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
        var projectName = await db.ApiProjects
            .Where(p => p.Id == run.ApiProjectId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct);

        return TypedResults.Ok(ToResponse(run, projectName));
    }

    private static async Task<IResult> CancelAsync(
        Guid organizationId,
        Guid runId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Member, ct) is { } denied)
        {
            return denied;
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
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct) is { } denied)
        {
            return denied;
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
        // Viewer, like every other read of this run — its detail, and the step logs that
        // describe how this very tarball was built. Member was a reasonable floor while this
        // handed out a presigned bucket URL, which is a credential rather than a read; now
        // that the bytes are streamed, holding the artifact above the logs that narrate it
        // guarded nothing and left the button on the run page refusing a whole role class.
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct) is { } denied)
        {
            return denied;
        }

        var run = await db.PipelineRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId && r.OrganizationId == organizationId, ct);

        if (run?.ArtifactStorageKey is null)
        {
            return ApiResults.NotFound("An artifact for this run");
        }

        try
        {
            // Streamed through the API rather than handed out as a presigned bucket URL, the
            // way spec content and step logs already are. A presigned URL is a bearer token
            // in a link: anyone holding it reads the object for as long as it lasts, from a
            // bucket that then has to be reachable from a browser. Proxying costs this
            // process the bytes and buys a download that is authorized per request, against
            // a bucket with no public surface and no CORS policy at all.
            var content = await storage.GetAsync(run.ArtifactStorageKey, ct);

            // The key's last segment is the name the runner packed the tarball under, so the
            // saved file keeps it instead of being named after the run's id.
            var fileName = run.ArtifactStorageKey[(run.ArtifactStorageKey.LastIndexOf('/') + 1)..];

            // Read from the name rather than assumed. Hard-coding gzip was true of npm's
            // tarball and false of every other package this platform will build.
            return TypedResults.File(content, ArtifactMediaTypes.ForFileName(fileName), fileName);
        }
        catch (ObjectNotFoundException)
        {
            // The row names an artifact the bucket no longer holds — a lifecycle rule that
            // swept it, or a restore that missed it. Not a server error to the caller.
            return ApiResults.NotFound("An artifact for this run");
        }
    }

    private static RunResponse ToResponse(PipelineRun run, string? projectName = null) =>
        new(run.Id,
            run.ApiProjectId,
            projectName,
            run.SpecVersionId,
            run.SpecSequence,
            run.SdkTargetId,
            run.TargetLanguage,
            run.TargetPackageName,
            run.TargetRegistry,
            run.TargetAutoPublish,
            run.TargetRegistryUrl,
            run.TargetCredentialLabel,
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
