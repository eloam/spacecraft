using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Api.Features.Credentials;

/// <summary>One SDK target that publishes with a credential, and where it lives.</summary>
public sealed record CredentialUsageResponse(
    Guid TargetId,
    string PackageName,
    SdkLanguage Language,
    Guid ProjectId,
    string ProjectName,
    string ProjectSlug);
