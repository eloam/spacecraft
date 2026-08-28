using SpaceCraft.Domain.Common;

namespace SpaceCraft.Domain.Pipelines;

/// <summary>
/// One stage of a run. Logs are kept in object storage and referenced by key — build
/// output can run to megabytes and has no business sitting in a Postgres column.
/// </summary>
public sealed class PipelineStep : Entity
{
    private PipelineStep() { }

    internal PipelineStep(Guid pipelineRunId, StepKind kind, int ordinal)
    {
        PipelineRunId = pipelineRunId;
        Kind = kind;
        Ordinal = ordinal;
    }

    public Guid PipelineRunId { get; private set; }
    public StepKind Kind { get; private set; }
    public int Ordinal { get; private set; }
    public StepStatus Status { get; private set; } = StepStatus.Pending;

    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Object storage key for this step's captured stdout/stderr.</summary>
    public string? LogStorageKey { get; private set; }

    /// <summary>Operator-facing failure summary. Never contains credentials.</summary>
    public string? FailureReason { get; private set; }

    public TimeSpan? Duration => StartedAt is null || CompletedAt is null
        ? null
        : CompletedAt - StartedAt;

    public void Start()
    {
        Status = StepStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Succeed(string? logStorageKey = null)
    {
        Status = StepStatus.Succeeded;
        CompletedAt = DateTimeOffset.UtcNow;
        LogStorageKey = logStorageKey ?? LogStorageKey;
        Touch();
    }

    /// <summary>
    /// Points at the same bytes under a different key, after they have been copied there.
    ///
    /// Separate from the methods that record work being done: nothing about this step
    /// changed, only where its object lives.
    /// </summary>
    public void RelocateLogTo(string storageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        LogStorageKey = storageKey;
        Touch();
    }

    public void Fail(string reason, string? logStorageKey = null)
    {
        Status = StepStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
        FailureReason = reason;
        LogStorageKey = logStorageKey ?? LogStorageKey;
        Touch();
    }

    public void Skip(string? reason = null)
    {
        Status = StepStatus.Skipped;
        CompletedAt = DateTimeOffset.UtcNow;
        FailureReason = reason;
        Touch();
    }
}
