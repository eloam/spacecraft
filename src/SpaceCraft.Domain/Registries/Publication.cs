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

    /// <summary>
    /// Points an existing record at the run retrying it, and clears what the last attempt
    /// concluded.
    ///
    /// <para>
    /// A registry holds one artifact per version, so (target, version) is unique here too.
    /// Without this, an attempt that never reached the registry — a lost credential, a
    /// crashed runner — left a Pending row that made every retry of that version fail on the
    /// unique index, with a raw database error rather than anything a reader could act on.
    /// </para>
    /// </summary>
    public void Retry(Guid pipelineRunId)
    {
        PipelineRunId = pipelineRunId;
        Status = PublicationStatus.Pending;
        FailureReason = null;
        RegistryUrl = null;
        CompletedAt = null;
        Touch();
    }

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
