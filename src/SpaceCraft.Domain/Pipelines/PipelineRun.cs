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
    /// <summary>Matches the failure_reason column; longer values are cut rather than refused.</summary>
    private const int ReasonLimit = 2000;

    /// <summary>How long a claim stays valid before another worker may steal the run.</summary>
    /// <summary>
    /// How long a claim holds before the reaper may give the run to somebody else.
    /// </summary>
    /// <remarks>
    /// Renewed before each step, so this only has to outlast the longest single one — and it
    /// did not. The build step is allowed fifteen minutes and the lease was fifteen minutes,
    /// leaving no margin at all: a build that used its budget was declared abandoned while
    /// still running, and the reaper — which sweeps every minute — handed the same run to a
    /// second worker. Two builds of one package, and whichever finished last wrote the result.
    /// npm never came close, which is why it never happened; a cold Gradle or Maven resolve
    /// would reach it routinely.
    /// <para>
    /// PipelineTimeoutTests pins the margin, so raising a step's timeout past this fails the
    /// build rather than reintroducing the race.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(25);

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
        int specSequence,
        SdkTarget target,
        string? targetRegistryUrl,
        string? targetCredentialLabel,
        RunTrigger trigger,
        bool publishRequested,
        Guid? triggeredByUserId = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        OrganizationId = organizationId;
        ApiProjectId = apiProjectId;
        SpecVersionId = specVersionId;
        SpecSequence = specSequence;
        SdkTargetId = target.Id;
        TargetLanguage = target.Language;
        TargetPackageName = target.PackageName;
        TargetRegistry = target.Registry;
        TargetAutoPublish = target.AutoPublish;
        TargetRegistryUrl = targetRegistryUrl;
        TargetCredentialLabel = targetCredentialLabel;
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

    /// <summary>
    /// The spec version's number as the project counts them — "#4" — copied for the same
    /// reason as the target's settings: a version can be deleted, and a run that cannot name
    /// the document it was built from cannot answer the first question asked of a failure.
    /// </summary>
    public int SpecSequence { get; private set; }
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

    /// <summary>
    /// The credential's label as it read then. Names which token was used in words someone
    /// recognises, which the URL does not, and outlives the credential being renamed or
    /// deleted — the two reasons this is a copy rather than a join.
    /// </summary>
    public string? TargetCredentialLabel { get; private set; }
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

    /// <summary>
    /// When a job was last started for this run, or null when none has been.
    /// </summary>
    /// <remarks>
    /// Durable rather than held in a dispatcher's memory, and that is the whole point: several
    /// API instances sweep the same table, and memory in one of them says nothing to the
    /// others. Without this column three instances start three jobs for one run — harmless,
    /// because claiming filters on <see cref="RunStatus.Queued"/> under SKIP LOCKED and the
    /// losers exit having done nothing, but it spends three executions against a parallel-run
    /// ceiling of ten. It also survives a redeploy, where memory would re-dispatch everything
    /// still in flight.
    /// <para>
    /// A dispatch is not a claim: the run stays <see cref="RunStatus.Queued"/> until a worker
    /// takes it, because the job may never boot. This only records that one was asked for.
    /// </para>
    /// </remarks>
    public DateTimeOffset? DispatchedAt { get; private set; }

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

    /// <summary>Records that a job has been asked to execute this run.</summary>
    public void MarkDispatched(DateTimeOffset now)
    {
        DispatchedAt = now;
        Touch();
    }

    /// <summary>
    /// Undoes a dispatch that never happened, so the next sweep tries again at once instead of
    /// waiting out the redispatch window for a job that was never started.
    /// </summary>
    public void ClearDispatch()
    {
        DispatchedAt = null;
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

    /// <summary>
    /// Marks the run finished, unless it has already finished.
    /// </summary>
    /// <remarks>
    /// The guard is the point. A run cancelled while a worker was mid-flight used to be
    /// overwritten with Succeeded when that worker reached the end, so the cancellation left
    /// no trace at all — and the package had been published in the meantime. A terminal
    /// status is a decision somebody made; nothing later gets to unmake it silently.
    /// </remarks>
    public void Succeed()
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"Cannot succeed a run in status {Status}.");
        }

        Status = RunStatus.Succeeded;
        CompletedAt = DateTimeOffset.UtcNow;
        ClaimedBy = null;
        LeaseExpiresAt = null;
        Touch();
    }

    public void Fail(string reason)
    {
        Status = RunStatus.Failed;
        FailureReason = Text.Clamp(reason, ReasonLimit);
        CompletedAt = DateTimeOffset.UtcNow;
        ClaimedBy = null;
        LeaseExpiresAt = null;

        // A step that never started is skipped; one that was under way when the run died is
        // failed with the run's own reason. Only the first was settled before, so a run that
        // faulted mid-step — an exception, an exhausted attempt — left that step at Running
        // for good. Polling stops at a terminal run, so the interface showed a spinner that
        // could never resolve, on a run everything else called finished.
        foreach (var step in _steps.Where(s => s.Status is StepStatus.Pending))
        {
            step.Skip("Run failed before this step.");
        }

        foreach (var step in _steps.Where(s => s.Status is StepStatus.Running))
        {
            step.Fail(reason);
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

        // Forgotten deliberately: a requeued run should be dispatched again at once, not after
        // the redispatch window of the job that just died on it.
        DispatchedAt = null;

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
        FailureReason = Text.Clamp(reason, ReasonLimit);
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
