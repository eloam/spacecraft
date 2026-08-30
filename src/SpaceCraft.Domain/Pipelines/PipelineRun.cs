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

    /// <summary>
    /// The target is passed whole, and its settings are copied rather than referenced.
    /// <para>
    /// A run is history. Reading the target's language, package name, registry, publishing
    /// mode or address back off the target would show what it says today — so a target
    /// renamed, repointed at another registry, or switched from manual to automatic would
    /// rewrite every run that came before, and a deleted one would leave them describing
    /// nothing. Copied here, a run keeps saying what it actually did.
    /// </para>
    /// <para>
    /// <paramref name="targetRegistryUrl"/> comes from the credential the target names, read
    /// at the same moment for the same reason: the endpoint travels with the credential, and
    /// revoking or repointing one must not change where an old run says it published.
    /// </para>
    /// </summary>
    public PipelineRun(
        Guid organizationId,
        Guid apiProjectId,
        Guid specVersionId,
        SdkTarget target,
        string? targetRegistryUrl,
        RunTrigger trigger,
        bool publishRequested,
        Guid? triggeredByUserId = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        OrganizationId = organizationId;
        ApiProjectId = apiProjectId;
        SpecVersionId = specVersionId;
        SdkTargetId = target.Id;
        TargetLanguage = target.Language;
        TargetPackageName = target.PackageName;
        TargetRegistry = target.Registry;
        TargetAutoPublish = target.AutoPublish;
        TargetRegistryUrl = targetRegistryUrl;
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

    /// <summary>
    /// What the target said when this run was queued. See the constructor for why these are
    /// copies rather than a lookup.
    /// </summary>
    public SdkLanguage TargetLanguage { get; private set; }

    /// <inheritdoc cref="TargetLanguage"/>
    public string TargetPackageName { get; private set; } = "";

    /// <inheritdoc cref="TargetLanguage"/>
    public RegistryKind TargetRegistry { get; private set; }

    /// <inheritdoc cref="TargetLanguage"/>
    public bool TargetAutoPublish { get; private set; }

    /// <summary>
    /// Where the credential this target named pointed when the run was queued. Null when the
    /// target had none, which is also why the run could not publish.
    /// </summary>
    public string? TargetRegistryUrl { get; private set; }
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

    /// <summary>
    /// Points at the same bytes under a different key, after they have been copied there.
    ///
    /// Separate from the methods that record work being done: nothing about this run
    /// changed, only where its object lives.
    /// </summary>
    public void RelocateArtifactTo(string storageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

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
