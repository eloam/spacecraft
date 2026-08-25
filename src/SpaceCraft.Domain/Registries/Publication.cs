using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Domain.Registries;

public enum PublicationStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3,

    /// <summary>
    /// The registry already had this exact version. Not an error: registries are
    /// append-only, so re-running a pipeline must be a no-op rather than a failure.
    /// </summary>
    AlreadyExists = 4,
}

/// <summary>An attempt to push one built SDK version to one registry.</summary>
public sealed class Publication : Entity
{
    private Publication() { }

    public Publication(
        Guid organizationId,
        Guid pipelineRunId,
        Guid sdkTargetId,
        RegistryKind registry,
        string packageName,
        string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        OrganizationId = organizationId;
        PipelineRunId = pipelineRunId;
        SdkTargetId = sdkTargetId;
        Registry = registry;
        PackageName = packageName;
        Version = version;
    }

    public Guid OrganizationId { get; private set; }
    public Guid PipelineRunId { get; private set; }
    public Guid SdkTargetId { get; private set; }
    public RegistryKind Registry { get; private set; }
    public string PackageName { get; private set; } = null!;
    public string Version { get; private set; } = null!;
    public PublicationStatus Status { get; private set; } = PublicationStatus.Pending;

    /// <summary>Public URL of the published package, when the registry gives us one.</summary>
    public string? RegistryUrl { get; private set; }

    public string? FailureReason { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public void Succeed(string? registryUrl)
    {
        Status = PublicationStatus.Succeeded;
        RegistryUrl = registryUrl;
        CompletedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void MarkAlreadyExists()
    {
        Status = PublicationStatus.AlreadyExists;
        CompletedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Fail(string reason)
    {
        Status = PublicationStatus.Failed;
        FailureReason = reason;
        CompletedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
