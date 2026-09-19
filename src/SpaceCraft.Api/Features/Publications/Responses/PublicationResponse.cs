using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;

namespace SpaceCraft.Api.Features.Publications.Responses;

/// <param name="Id">The publication's id.</param>
/// <param name="PipelineRunId">The run that produced the package.</param>
/// <param name="SpecVersionId">
/// The document this package was generated from. Carried so a caller can say which spec
/// version is live without joining publications to runs itself.
/// </param>
/// <param name="SdkTargetId">The target it was published for.</param>
/// <param name="ApiProjectId">The project that target belongs to, when it still exists.</param>
/// <param name="ProjectName">That project's name.</param>
/// <param name="Registry">The registry it went to.</param>
/// <param name="PackageName">The package name there.</param>
/// <param name="Version">The version pushed.</param>
/// <param name="Status">Whether the push succeeded, failed or is still under way.</param>
/// <param name="RegistryUrl">Where the package lives, once it does.</param>
/// <param name="FailureReason">Why the push failed, when it did.</param>
/// <param name="CreatedAt">When the publication started.</param>
/// <param name="CompletedAt">When it finished, either way.</param>
public sealed record PublicationResponse(
    Guid Id,
    Guid PipelineRunId,
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
