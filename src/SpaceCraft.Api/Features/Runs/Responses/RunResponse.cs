using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Api.Features.Runs.Responses;

/// <param name="Id">The run's id.</param>
/// <param name="ApiProjectId">The project it belongs to.</param>
/// <param name="ProjectName">Carried so a caller can name the project without a second request.</param>
/// <param name="ProjectSlug">
/// And its slug, so a caller can link to the project without one either. The id above is
/// how the API is addressed; the slug is how the client is, and a link built from the id
/// would put a guid in front of somebody.
/// </param>
/// <param name="SpecVersionId">The document it was generated from.</param>
/// <param name="SpecSequence">Which version of the document, as the project numbers them.</param>
/// <param name="SdkTargetId">The target it was generated for.</param>
/// <param name="TargetLanguage">
/// What the SDK target said when this run was queued, not what it says now. A target
/// renamed or repointed since would otherwise rewrite the history of every run before it.
/// </param>
/// <param name="TargetPackageName">The package name, as the target read when this run was queued.</param>
/// <param name="TargetRegistry">The registry, as the target read when this run was queued.</param>
/// <param name="TargetAutoPublish">The auto-publish setting, as the target read when this run was queued.</param>
/// <param name="TargetRegistryUrl">Where its credential pointed then; null when it had none.</param>
/// <param name="TargetCredentialLabel">The credential's label as it read then; null when the target had none.</param>
/// <param name="Status">Where the run is in its life.</param>
/// <param name="Trigger">What queued it.</param>
/// <param name="PublishRequested">Whether it was asked to publish as well as build.</param>
/// <param name="ResolvedVersion">The version the package took, once it was decided.</param>
/// <param name="FailureReason">Why it failed, when it did.</param>
/// <param name="AttemptCount">How many times a worker has picked it up.</param>
/// <param name="QueuedAt">When it was queued.</param>
/// <param name="StartedAt">When a worker first took it.</param>
/// <param name="CompletedAt">When it reached a terminal status.</param>
/// <param name="HasArtifact">Whether a packaged SDK can be downloaded for it.</param>
/// <param name="Steps">Its steps, in pipeline order.</param>
public sealed record RunResponse(
    Guid Id,
    Guid ApiProjectId,
    string? ProjectName,
    string? ProjectSlug,
    Guid SpecVersionId,
    int SpecSequence,
    Guid SdkTargetId,
    SdkLanguage TargetLanguage,
    string TargetPackageName,
    RegistryKind TargetRegistry,
    bool TargetAutoPublish,
    string? TargetRegistryUrl,
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
    IReadOnlyList<StepResponse> Steps)
{
    public static RunResponse From(
        PipelineRun run, string? projectName = null, string? projectSlug = null) =>
        new(run.Id,
            run.ApiProjectId,
            projectName,
            projectSlug,
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
