using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Api.Features.Runs.Responses;

public sealed record RunResponse(
    Guid Id,
    Guid ApiProjectId,
    /// <summary>Carried so a caller can name the project without a second request.</summary>
    string? ProjectName,
    /// <summary>
    /// And its slug, so a caller can link to the project without one either. The id above is
    /// how the API is addressed; the slug is how the client is, and a link built from the id
    /// would put a guid in front of somebody.
    /// </summary>
    string? ProjectSlug,
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
