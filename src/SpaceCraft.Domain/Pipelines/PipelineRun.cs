using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Domain.Pipelines;

/// <summary>
/// One pass of validate → generate → build → publish for a single SDK target.
/// Doubles as the queue row: workers claim runs with FOR UPDATE SKIP LOCKED and hold
/// a time-boxed lease, so a worker that dies mid-build releases its run instead of
/// wedging it in Running forever.
/// </summary>
public sealed class PipelineRun : Entity
{
    /// <summary>How long a claim stays valid before another worker may steal the run.</summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(15);

    public const int DefaultMaxAttempts = 3;

    private readonly List<PipelineStep> _steps = [];

    private PipelineRun() { }

    public PipelineRun(
        Guid organizationId,
        Guid apiProjectId,
        Guid specVersionId,
        Guid sdkTargetId,
        RunTrigger trigger,
        bool publishRequested,
        Guid? triggeredByUserId = null)
    {
        OrganizationId = organizationId;
        ApiProjectId = apiProjectId;
        SpecVersionId = specVersionId;
        SdkTargetId = sdkTargetId;
        Trigger = trigger;
        PublishRequested = publishRequested;
        TriggeredByUserId = triggeredByUserId;
        QueuedAt = DateTimeOffset.UtcNow;

        var ordinal = 0;
        _steps.Add(new PipelineStep(Id, StepKind.Validate, ordinal++));
        _steps.Add(new PipelineStep(Id, StepKind.Generate, ordinal++));
        _steps.Add(new PipelineStep(Id, StepKind.Build, ordinal++));
        _steps.Add(new PipelineStep(Id, StepKind.Publish, ordinal));

        if (!publishRequested)
        {
            _steps[^1].Skip("Publish not requested for this run.");
        }
    }

    public Guid OrganizationId { get; private set; }
    public Guid ApiProjectId { get; private set; }
    public Guid SpecVersionId { get; private set; }
    public Guid SdkTargetId { get; private set; }
    public Guid? TriggeredByUserId { get; private set; }

    public RunTrigger Trigger { get; private set; }
    public RunStatus Status { get; private set; } = RunStatus.Queued;

    /// <summary>Whether this run should push to a registry, or stop after build.</summary>
    public bool PublishRequested { get; private set; }

    /// <summary>Version the SDK is generated as — resolved from the spec's info.version.</summary>
    public string? ResolvedVersion { get; private set; }

    public DateTimeOffset QueuedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Identity of the worker holding the claim, for debugging stuck runs.</summary>
    public string? ClaimedBy { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; } = DefaultMaxAttempts;

    public string? FailureReason { get; private set; }

    /// <summary>Object storage key of the built package artifact, once Build succeeds.</summary>
    public string? ArtifactStorageKey { get; private set; }

    /// <summary>Backing collection, mapped by EF. Use <see cref="OrderedSteps"/> for display order.</summary>
    public IReadOnlyCollection<PipelineStep> Steps => _steps;

    public IReadOnlyList<PipelineStep> OrderedSteps() => [.. _steps.OrderBy(s => s.Ordinal)];

    public PipelineStep Step(StepKind kind) => _steps.Single(s => s.Kind == kind);

    public bool IsTerminal => Status is RunStatus.Succeeded or RunStatus.Failed or RunStatus.Cancelled;

    /// <summary>Marks the run as picked up by a worker and extends the lease.</summary>
    public void Claim(string workerId, DateTimeOffset now)
    {
        if (Status is not RunStatus.Queued)
        {
            throw new InvalidOperationException($"Cannot claim a run in status {Status}.");
        }

        Status = RunStatus.Running;
        ClaimedBy = workerId;
        LeaseExpiresAt = now + LeaseDuration;
        StartedAt ??= now;
        AttemptCount++;
        Touch();
    }

    /// <summary>Pushes the lease out while a long build is still making progress.</summary>
    public void RenewLease(DateTimeOffset now)
    {
        if (Status is not RunStatus.Running)
        {
            throw new InvalidOperationException($"Cannot renew the lease of a run in status {Status}.");
        }

        LeaseExpiresAt = now + LeaseDuration;
        Touch();
    }

    public void SetResolvedVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ResolvedVersion = version;
        Touch();
    }

    public void SetArtifact(string storageKey)
    {
        ArtifactStorageKey = storageKey;
        Touch();
    }

    public void Succeed()
    {
        Status = RunStatus.Succeeded;
        CompletedAt = DateTimeOffset.UtcNow;
        ClaimedBy = null;
        LeaseExpiresAt = null;
        Touch();
    }

    public void Fail(string reason)
    {
        Status = RunStatus.Failed;
        FailureReason = reason;
        CompletedAt = DateTimeOffset.UtcNow;
        ClaimedBy = null;
        LeaseExpiresAt = null;

        foreach (var step in _steps.Where(s => s.Status is StepStatus.Pending))
        {
            step.Skip("Run failed before this step.");
        }

        Touch();
    }

    /// <summary>
    /// Returns the run to the queue after a recoverable fault (worker crash, lease expiry).
    /// Once attempts are exhausted it fails for good rather than looping.
    /// </summary>
    public bool TryRequeue(string reason)
    {
        if (AttemptCount >= MaxAttempts)
        {
            Fail($"{reason} (gave up after {AttemptCount} attempts)");
            return false;
        }

        Status = RunStatus.Queued;
        ClaimedBy = null;
        LeaseExpiresAt = null;

        foreach (var step in _steps.Where(s => s.Status is StepStatus.Running))
        {
            step.Fail(reason);
        }

        Touch();
        return true;
    }

    public void Cancel(string reason)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"Cannot cancel a run in status {Status}.");
        }

        Status = RunStatus.Cancelled;
        FailureReason = reason;
        CompletedAt = DateTimeOffset.UtcNow;
        ClaimedBy = null;
        LeaseExpiresAt = null;

        foreach (var step in _steps.Where(s => s.Status is StepStatus.Pending or StepStatus.Running))
        {
            step.Skip(reason);
        }

        Touch();
    }
}
