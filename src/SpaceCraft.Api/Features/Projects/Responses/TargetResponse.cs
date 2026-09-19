using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Api.Features.Projects.Responses;

public sealed record TargetResponse(
    Guid Id,
    SdkLanguage Language,
    string PackageName,
    RegistryKind Registry,
    bool AutoPublish,
    Guid? RegistryCredentialId,
    string? LastPublishedVersion,
    DateTimeOffset? LastPublishedAt)
{
    public static TargetResponse From(SdkTarget target) =>
        new(target.Id,
            target.Language,
            target.PackageName,
            target.Registry,
            target.AutoPublish,
            target.RegistryCredentialId,
            target.LastPublishedVersion,
            target.LastPublishedAt);
}
