using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;

namespace SpaceCraft.Api.Features.Publications.Responses;

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
