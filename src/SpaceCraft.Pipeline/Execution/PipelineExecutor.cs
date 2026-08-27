using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
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

            if (!await RunGenerateStepAsync(run, spec, target, workspace, specFileName, clientName, version, ct))
            {
                return;
            }

            if (!await RunBuildStepAsync(run, target, workspace, version, ct))
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

    private async Task<string> DownloadSpecAsync(SpecVersion spec, RunWorkspace workspace, CancellationToken ct)
    {
        var extension = Path.GetExtension(spec.StorageKey) is { Length: > 0 } ext ? ext : ".yaml";
        var fileName = $"openapi{extension}";

        await using var source = await storage.GetAsync(spec.StorageKey, ct);
        await using var destination = File.Create(workspace.HostPath(RunWorkspace.SpecRelativePath(fileName)));
        await source.CopyToAsync(destination, ct);

        return fileName;
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

        var result = await specValidator.ValidateAsync(stream, specFileName, strict, ct);

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

        // Registries require a semantic version; real specs frequently do not carry one
        // (Keycloak's Admin API declares "1.0"). Normalising here means an unpublishable
        // version fails in validation, not after a fifteen-minute build.
        var resolvedVersion = SemanticVersion.TryNormalize(result.ApiVersion);
        if (resolvedVersion is null)
        {
            var reason =
                $"info.version is '{result.ApiVersion}', which cannot be turned into a semantic version. "
                + "Package registries require major.minor.patch — set a version like 1.0.0 in the document.";

            step.Fail(reason, logKey);
            run.Fail(reason);
            await db.SaveChangesAsync(ct);
            return false;
        }

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

        var artifactKey = StorageKeys.Artifact(run.OrganizationId, run.Id, outcome.ArtifactFileName);

        await using (var artifact = File.OpenRead(artifactPath))
        {
            await storage.PutAsync(artifactKey, artifact, "application/gzip", ct);
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

        var publisher = publishers.FirstOrDefault(p => p.Registry == target.Registry);
        if (publisher is null)
        {
            return await FailStepAsync(run, step, $"No publisher is registered for {target.Registry}.", null, ct);
        }

        var credential = await db.RegistryCredentials
            .Where(c => c.OrganizationId == run.OrganizationId && c.Registry == target.Registry && !c.IsRevoked)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (credential is null || !credential.IsUsable(DateTimeOffset.UtcNow))
        {
            return await FailStepAsync(
                run, step,
                $"No usable {target.Registry} credential is configured for this organization.",
                null, ct);
        }

        var publication = new Publication(
            run.OrganizationId, run.Id, target.Id, target.Registry, target.PackageName, version);

        db.Publications.Add(publication);
        await db.SaveChangesAsync(ct);

        var artifactFileName = Path.GetFileName(run.ArtifactStorageKey!);

        // The decrypted token exists only for the duration of this call.
        var outcome = await publisher.PublishAsync(new PublishRequest
        {
            WorkspaceRoot = workspace.Root,
            ArtifactDirectory = RunWorkspace.ArtifactRelativePath,
            ArtifactFileName = artifactFileName,
            PackageName = target.PackageName,
            Version = version,
            Token = secretProtector.Unprotect(credential.CipherText),
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

        var key = StorageKeys.StepLog(run.OrganizationId, run.Id, kind.ToString());

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
