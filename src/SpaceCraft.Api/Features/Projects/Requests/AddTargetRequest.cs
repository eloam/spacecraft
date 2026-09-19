using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Api.Features.Projects.Requests;

public sealed record AddTargetRequest(
    SdkLanguage Language,
    string PackageName,
    RegistryKind? Registry,
    bool AutoPublish,
    /// <summary>Which credential publishes it. Null while the organization has only one.</summary>
    Guid? RegistryCredentialId = null);
