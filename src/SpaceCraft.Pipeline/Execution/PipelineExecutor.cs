using SpaceCraft.Domain.Notifications;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Notifications;
using SpaceCraft.Infrastructure.OpenApi;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Security;
using SpaceCraft.Infrastructure.Storage;
using SpaceCraft.Pipeline.Building;
using SpaceCraft.Pipeline.Generation;
using SpaceCraft.Pipeline.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Pipeline.Execution;

public interface IPipelineExecutor
{
    Task ExecuteAsync(Guid runId, CancellationToken ct = default);
}

/// <summary>
/// Drives one run through validate → generate → build → publish.
/// <para>
/// Each step records its own status and log before the next begins, so a run that dies
/// halfway still shows an operator exactly how far it got. Failures are recorded on the
/// run rather than thrown: a broken spec or a failing compile is an expected outcome of
/// this system, not an exception.
/// </para>
/// </summary>
public sealed class PipelineExecutor(
    SpaceCraftDbContext db,
    IObjectStorage storage,
    ISpecValidator specValidator,
    IEnumerable<ISdkGenerator> generators,
    IEnumerable<IPackageScaffolder> scaffolders,
    IEnumerable<ISdkBuilder> builders,
    IEnumerable<ISdkPublisher> publishers,
    ISecretProtector secretProtector,
    INotificationPublisher notifications,
    IOptions<PipelineOptions> options,
    ILogger<PipelineExecutor> logger) : IPipelineExecutor
{
    private readonly PipelineOptions _options = options.Value;

    public async Task ExecuteAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await db.PipelineRuns
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new InvalidOperationException($"Run {runId} not found.");

        var spec = await db.SpecVersions.FirstAsync(s => s.Id == run.SpecVersionId, ct);
        var target = await db.SdkTargets.FirstAsync(t => t.Id == run.SdkTargetId, ct);
        var project = await db.ApiProjects.FirstAsync(p => p.Id == run.ApiProjectId, ct);

        await DriveAsync(run, spec, target, project, ct);

        // Outside the drive, because every step that fails returns straight out of it — and a
        // notification raised from inside would have to be raised at each of those exits.
        //
        // Succeeded and Failed only. A cancelled run finished too, but somebody just pressed
        // the button that cancelled it; telling them what they did is the kind of message
        // that teaches people to ignore the rest.
        if (run.Status is RunStatus.Succeeded or RunStatus.Failed)
        {
            await NotifyAsync(run, project, ct);
        }
    }

    private async Task DriveAsync(
        PipelineRun run,
        SpecVersion spec,
        SdkTarget target,
        ApiProject project,
        CancellationToken ct)
    {
        using var workspace = RunWorkspace.Create(_options.WorkspaceRoot, run.Id, _options.RetainWorkspaces);

        try
        {
            var specFileName = await DownloadSpecAsync(spec, workspace, ct);

            if (!await RunValidateStepAsync(run, spec, workspace, specFileName, ct))
            {
                return;
            }

            var version = run.ResolvedVersion
                ?? throw new InvalidOperationException("Validation must resolve a version before generation.");

            var clientName = KiotaSdkGenerator.DeriveClientName(spec.ApiTitle, target.PackageName);

            if (await CancelledAsync(run, ct))
            {
                return;
            }

            if (!await RunGenerateStepAsync(run, spec, target, workspace, specFileName, clientName, version, ct))
            {
                return;
            }

            if (await CancelledAsync(run, ct))
            {
                return;
            }

            if (!await RunBuildStepAsync(run, target, workspace, version, ct))
            {
                return;
            }

            if (await CancelledAsync(run, ct))
            {
                return;
            }

            if (!await RunPublishStepAsync(run, target, workspace, version, ct))
            {
                return;
            }

            run.Succeed();
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Run {RunId} succeeded: {Package}@{Version} ({Project})",
                run.Id, target.PackageName, version, project.Slug);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown, not failure. Leave the run claimed: the lease will lapse and another
            // worker will pick it up rather than burning an attempt.
            logger.LogInformation("Run {RunId} interrupted by shutdown; leaving it to lease expiry", run.Id);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {RunId} failed unexpectedly", run.Id);
            run.Fail($"Unexpected error: {ex.Message}");
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Tells whoever started the run how it went.
    /// </summary>
    /// <remarks>
    /// Best effort, deliberately. The run is already saved by the time this runs, and an
    /// exception escaping here would reach the worker as a run that crashed — which is
    /// requeued, and requeueing a run that has already published means publishing again.
    /// A notification is never worth that.
    /// </remarks>
    private async Task NotifyAsync(PipelineRun run, ApiProject project, CancellationToken ct)
    {
        try
        {
            var succeeded = run.Status is RunStatus.Succeeded;
            var package = run.TargetPackageName;

            var detail = succeeded
                ? run.PublishRequested
                    ? $"{package} {run.ResolvedVersion} was published to {run.TargetRegistry}."
                    : $"{package} {run.ResolvedVersion} was built. Publishing was not requested."
                : Failure(run);

            var content = new NotificationContent(
                NotificationKind.RunCompleted,
                succeeded ? $"Run succeeded — {package}" : $"Run failed — {package}",
                $"{project.Name}, spec #{run.SpecSequence}, {run.TargetLanguage}.",
                detail,
                $"/orgs/{run.OrganizationId}/runs/{run.Id}",
                "View the run");

            if (run.TriggeredByUserId is { } author)
            {
                await notifications.PublishToMemberAsync(run.OrganizationId, author, content, ct);
            }
            else
            {
                // Nobody pressed anything — an automatic run. Staying silent would make the
                // runs nobody is watching the ones nobody hears about.
                await notifications.PublishToGovernanceAsync(run.OrganizationId, actor: null, content, ct);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Run {RunId} finished during shutdown; its notification was not raised", run.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {RunId} finished but its notification could not be raised", run.Id);
        }
    }

    /// <summary>
    /// What went wrong, and where. The reason alone answers "why"; the step answers "how far
    /// did it get", which is the first thing anybody asks of a failed run.
    /// </summary>
    private static string Failure(PipelineRun run)
    {
        var reason = run.FailureReason ?? "No reason was recorded.";
        var step = run.Steps.FirstOrDefault(s => s.Status is StepStatus.Failed);

        return step is null ? reason : $"The {step.Kind} step failed. {reason}";
    }

    private async Task<string> DownloadSpecAsync(SpecVersion spec, RunWorkspace workspace, CancellationToken ct)
    {
        var extension = Path.GetExtension(spec.StorageKey) is { Length: > 0 } ext ? ext : ".yaml";
        var fileName = $"openapi{extension}";

        await using var source = await storage.GetAsync(spec.StorageKey, ct);
        await using var destination = File.Create(workspace.HostPath(RunWorkspace.SpecRelativePath(fileName)));
        await source.CopyToAsync(destination, ct);

        return fileName;
    }

    /// <summary>
    /// True when someone cancelled this run while the worker was between steps.
    /// </summary>
    /// <remarks>
    /// Read from the database, not from the entity in hand: cancellation is written by the API
    /// in another process, so the copy loaded when this run was claimed can never learn of it.
    /// Nothing in the pipeline used to ask, which made Cancel a button that changed a row and
    /// stopped nothing — the build carried on, the package was published, and Succeeded was
    /// written over Cancelled at the end.
    /// <para>
    /// Between steps rather than inside them. A step already running is left to finish; what
    /// matters is not starting the next one, and the next one is sometimes a publish.
    /// </para>
    /// </remarks>
    private async Task<bool> CancelledAsync(PipelineRun run, CancellationToken ct)
    {
        var status = await db.PipelineRuns
            .AsNoTracking()
            .Where(r => r.Id == run.Id)
            .Select(r => r.Status)
            .FirstOrDefaultAsync(ct);

        if (status != RunStatus.Cancelled)
        {
            return false;
        }

        logger.LogInformation("Run {RunId} was cancelled; stopping before the next step", run.Id);
        return true;
    }

    private async Task<bool> RunValidateStepAsync(
        PipelineRun run,
        SpecVersion spec,
        RunWorkspace workspace,
        string specFileName,
        CancellationToken ct)
    {
        var step = run.Step(StepKind.Validate);
        step.Start();
        await db.SaveChangesAsync(ct);

        await using var stream = File.OpenRead(workspace.HostPath(RunWorkspace.SpecRelativePath(specFileName)));
        // The project's own setting, so a run validates a document exactly the way its
        // upload did rather than by a second, looser standard.
        var strict = await db.ApiProjects
            .Where(p => p.Id == spec.ApiProjectId)
            .Select(p => p.StrictValidation)
            .FirstOrDefaultAsync(ct);

        // The version to be judged against is the one before this document in its project,
        // not simply the latest — revalidating an old spec must not compare it to its own
        // successors.
        var previousApiVersion = await db.SpecVersions
            .Where(s => s.ApiProjectId == spec.ApiProjectId && s.Sequence < spec.Sequence)
            .OrderByDescending(s => s.Sequence)
            .Select(s => s.ApiVersion)
            .FirstOrDefaultAsync(ct);

        var result = await specValidator.ValidateAsync(
            stream, specFileName, strict, previousApiVersion, ct);

        var log = string.Join('\n', result.Diagnostics.Select(d =>
            $"[{d.Severity.ToString().ToLowerInvariant()}] {d.JsonPointer ?? "-"} {d.Message}"));

        var logKey = await StoreLogAsync(run, StepKind.Validate, log, ct);

        if (!result.IsGeneratable)
        {
            var reason = $"Specification is not valid: {result.ErrorCount} error(s).";
            step.Fail(reason, logKey);
            run.Fail(reason);
            await db.SaveChangesAsync(ct);
            return false;
        }

        if (string.IsNullOrWhiteSpace(result.ApiVersion))
        {
            const string Reason = "The specification has no info.version, so the SDK version cannot be resolved.";
            step.Fail(Reason, logKey);
            run.Fail(Reason);
            await db.SaveChangesAsync(ct);
            return false;
        }

        // Rendered for the registry this run publishes to, not once for every registry.
        // Applying npm's rules to all of them is wrong in both directions: 1.2.3.4 is an
        // ordinary NuGet version and a legal Maven one, while npm cannot express it; and
        // completing 1.0 to 1.0.0 is required for npm and wrong for Maven, where the two
        // name different artifacts. The run knows its registry from the target snapshot
        // taken when it was queued, so nothing has to be looked up to decide this.
        //
        // Still at validation rather than at publish: an unpublishable version should fail
        // before a build, not fifteen minutes after one.
        var rendering = RegistryVersion.Render(run.TargetRegistry, result.ApiVersion);
        if (rendering.Version is null)
        {
            // The registry that refused says why, and what to write instead.
            var reason = rendering.Refusal!;

            step.Fail(reason, logKey);
            run.Fail(reason);
            await db.SaveChangesAsync(ct);
            return false;
        }

        var resolvedVersion = rendering.Version;

        if (resolvedVersion != result.ApiVersion)
        {
            logger.LogInformation(
                "Normalised info.version '{Declared}' to '{Resolved}' for run {RunId}",
                result.ApiVersion, resolvedVersion, run.Id);
        }

        // The SDK version tracks the API version. Deriving it anywhere else would let the
        // package drift from the contract it was generated from.
        run.SetResolvedVersion(resolvedVersion);

        spec.MarkValidated(
            status: result.Status,
            apiTitle: result.ApiTitle,
            apiVersion: result.ApiVersion,
            openApiVersion: result.OpenApiVersion,
            informationCount: result.InformationCount,
            warningCount: result.WarningCount,
            errorCount: result.ErrorCount,
            diagnosticsJson: System.Text.Json.JsonSerializer.Serialize(result.Diagnostics));

        step.Succeed(logKey);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> RunGenerateStepAsync(
        PipelineRun run,
        SpecVersion spec,
        SdkTarget target,
        RunWorkspace workspace,
        string specFileName,
        string clientName,
        string version,
        CancellationToken ct)
    {
        var step = run.Step(StepKind.Generate);
        step.Start();
        run.RenewLease(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        var generator = generators.FirstOrDefault(g => g.Language == target.Language);
        if (generator is null)
        {
            return await FailStepAsync(run, step, $"No generator is registered for {target.Language}.", null, ct);
        }

        var outcome = await generator.GenerateAsync(new GenerationRequest
        {
            WorkspaceRoot = workspace.Root,
            SpecFilePath = RunWorkspace.SpecRelativePath(specFileName),
            OutputDirectory = RunWorkspace.SourceRelativePath,
            Language = target.Language,
            PackageName = target.PackageName,
            Version = version,
            ClientClassName = clientName,
            ApiTitle = spec.ApiTitle,
        }, ct);

        var logKey = await StoreLogAsync(run, StepKind.Generate, outcome.Output, ct);

        if (!outcome.Succeeded)
        {
            return await FailStepAsync(run, step, outcome.FailureReason ?? "Generation failed.", logKey, ct);
        }

        // Raw generated sources are not yet a package: add the manifest, build config and docs.
        var scaffolder = scaffolders.FirstOrDefault(s => s.Language == target.Language);
        if (scaffolder is null)
        {
            return await FailStepAsync(run, step, $"No package scaffolder is registered for {target.Language}.", logKey, ct);
        }

        await scaffolder.ScaffoldAsync(new ScaffoldRequest
        {
            SourceDirectory = workspace.HostPath(RunWorkspace.SourceRelativePath),
            WorkspaceRoot = workspace.Root,
            SpecRelativePath = RunWorkspace.SpecRelativePath(specFileName),
            PackageName = target.PackageName,
            Version = version,
            ClientClassName = clientName,
            ApiTitle = spec.ApiTitle,
            Registry = target.Registry,
        }, ct);

        step.Succeed(logKey);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> RunBuildStepAsync(
        PipelineRun run,
        SdkTarget target,
        RunWorkspace workspace,
        string version,
        CancellationToken ct)
    {
        var step = run.Step(StepKind.Build);
        step.Start();
        run.RenewLease(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        var builder = builders.FirstOrDefault(b => b.Language == target.Language);
        if (builder is null)
        {
            return await FailStepAsync(run, step, $"No builder is registered for {target.Language}.", null, ct);
        }

        var outcome = await builder.BuildAsync(new BuildRequest
        {
            WorkspaceRoot = workspace.Root,
            SourceDirectory = RunWorkspace.SourceRelativePath,
            ArtifactDirectory = RunWorkspace.ArtifactRelativePath,
            PackageName = target.PackageName,
            Version = version,
        }, ct);

        var logKey = await StoreLogAsync(run, StepKind.Build, outcome.Output, ct);

        if (!outcome.Succeeded || outcome.ArtifactFileName is null)
        {
            return await FailStepAsync(run, step, outcome.FailureReason ?? "Build failed.", logKey, ct);
        }

        // Persist the artifact so it can be downloaded, re-published, or inspected long
        // after the workspace is gone.
        var artifactPath = Path.Combine(
            workspace.HostPath(RunWorkspace.ArtifactRelativePath),
            outcome.ArtifactFileName);

        var artifactKey = StorageKeys.Artifact(
            run.OrganizationId, run.ApiProjectId, run.Id, outcome.ArtifactFileName);

        await using (var artifact = File.OpenRead(artifactPath))
        {
            await storage.PutAsync(artifactKey, artifact, outcome.ArtifactMediaType, ct);
        }

        run.SetArtifact(artifactKey);
        step.Succeed(logKey);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> RunPublishStepAsync(
        PipelineRun run,
        SdkTarget target,
        RunWorkspace workspace,
        string version,
        CancellationToken ct)
    {
        var step = run.Step(StepKind.Publish);

        if (!run.PublishRequested)
        {
            // Already marked Skipped when the run was created.
            return true;
        }

        step.Start();
        run.RenewLease(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        // From the run's own snapshot, not from the target as it stands now. The snapshot was
        // taken when the run was queued and exists for exactly this: a target repointed while
        // a run waited in the queue must not move where that run publishes. Read live, a
        // credential switched from a private mirror to public npm would have sent the package
        // there while the run's history went on naming the mirror.
        var publisher = publishers.FirstOrDefault(p => p.Registry == run.TargetRegistry);
        if (publisher is null)
        {
            return await FailStepAsync(run, step, $"No publisher is registered for {run.TargetRegistry}.", null, ct);
        }

        // Null is not "work it out": it is a target that publishes nowhere, which the SDK
        // target dialog states as a choice. Guessing when an organization had several
        // credentials was how a package once landed on a registry nobody picked.
        if (target.RegistryCredentialId is not { } chosen)
        {
            return await FailStepAsync(
                run, step,
                "This SDK target does not publish. Choose a credential on the target to say "
                + "where its packages go.",
                null, ct);
        }

        // Usable is part of the question, not a check afterwards: loading it and then
        // rejecting it failed the publish while saying nothing about what to do instead.
        var now = DateTimeOffset.UtcNow;
        var credential = await db.RegistryCredentials.FirstOrDefaultAsync(
            c => c.Id == chosen
                && c.OrganizationId == run.OrganizationId
                && c.Registry == target.Registry
                && !c.IsRevoked
                && (c.ExpiresAt == null || c.ExpiresAt > now),
            ct);

        if (credential is null)
        {
            // Named and gone: publishing with another one would send the package somewhere
            // nobody chose, since the endpoint travels with the credential.
            return await FailStepAsync(
                run, step,
                "The credential this target publishes with has been revoked, has expired, or "
                + "no longer exists. Choose another on the SDK target.",
                null, ct);
        }

        // Drift between what the run recorded and where the credential now points. Publishing
        // to either would be defensible and both are wrong: the run says one destination and
        // the target says another, and a registry does not take a package back. Refused, so
        // somebody decides rather than a race deciding.
        if (run.TargetRegistryUrl is { } recorded
            && !string.Equals(recorded, credential.RegistryUrl, StringComparison.OrdinalIgnoreCase))
        {
            return await FailStepAsync(
                run, step,
                $"This run was queued to publish to {recorded}, and the credential now points "
                + $"at {credential.RegistryUrl}. Start a new run to publish to the new address.",
                null, ct);
        }

        // One row per (target, version), because a registry holds one artifact per version.
        // A retry of the same version reuses it rather than inserting a second.
        var publication = await db.Publications
            .FirstOrDefaultAsync(p => p.SdkTargetId == target.Id && p.Version == version, ct);

        if (publication is null)
        {
            publication = new Publication(
                run.OrganizationId, run.Id, target.Id, target.Registry, target.PackageName, version);

            db.Publications.Add(publication);
        }
        else
        {
            publication.Retry(run.Id);
        }
        await db.SaveChangesAsync(ct);

        var artifactFileName = Path.GetFileName(run.ArtifactStorageKey!);

        // The decrypted secret exists only for the duration of this call. Parsed rather than
        // used raw: a credential stored before the document shape holds the bare token, and
        // both must keep working until the rewrite has run.
        var secret = RegistrySecret.Parse(secretProtector.Unprotect(credential.CipherText));

        // The decrypted token exists only for the duration of this call.
        var outcome = await publisher.PublishAsync(new PublishRequest
        {
            WorkspaceRoot = workspace.Root,
            ArtifactDirectory = RunWorkspace.ArtifactRelativePath,
            ArtifactFileName = artifactFileName,
            PackageName = run.TargetPackageName,
            Version = version,
            // The address the run recorded. Where it differs from the credential's current one
            // the run is refused above rather than sent to either, because a package landing
            // somewhere its own history denies is the outcome worth refusing outright.
            RegistryUrl = run.TargetRegistryUrl ?? credential.RegistryUrl,
            Token = secret.Token,
            Username = secret.Username,
        }, ct);

        var logKey = await StoreLogAsync(run, StepKind.Publish, outcome.Output, ct);
        credential.MarkUsed(DateTimeOffset.UtcNow);

        switch (outcome.Kind)
        {
            case PublishResultKind.Succeeded:
                publication.Succeed(outcome.RegistryUrl);
                target.RecordPublication(version, DateTimeOffset.UtcNow);
                break;

            case PublishResultKind.AlreadyExists:
                publication.MarkAlreadyExists();
                target.RecordPublication(version, DateTimeOffset.UtcNow);
                break;

            default:
                publication.Fail(outcome.FailureReason ?? "Publish failed.");
                return await FailStepAsync(run, step, outcome.FailureReason ?? "Publish failed.", logKey, ct);
        }

        step.Succeed(logKey);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> FailStepAsync(
        PipelineRun run,
        PipelineStep step,
        string reason,
        string? logKey,
        CancellationToken ct)
    {
        step.Fail(reason, logKey);
        run.Fail(reason);
        await db.SaveChangesAsync(ct);
        return false;
    }

    private async Task<string?> StoreLogAsync(PipelineRun run, StepKind kind, string content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var key = StorageKeys.StepLog(
            run.OrganizationId, run.ApiProjectId, run.Id, kind.ToString());

        try
        {
            await storage.PutTextAsync(key, content, "text/plain; charset=utf-8", ct);
            return key;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Losing a log must never lose the run's outcome.
            logger.LogWarning(ex, "Could not store {Step} log for run {RunId}", kind, run.Id);
            return null;
        }
    }
}
