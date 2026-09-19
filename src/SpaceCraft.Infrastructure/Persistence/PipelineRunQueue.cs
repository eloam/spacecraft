using SpaceCraft.Domain.Pipelines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SpaceCraft.Infrastructure.Persistence;

public interface IPipelineRunQueue
{
    /// <summary>
    /// Atomically takes the oldest queued run, or returns null when the queue is empty.
    /// </summary>
    Task<PipelineRun?> TryClaimAsync(string workerId, CancellationToken ct = default);

    /// <summary>
    /// Claims one specific queued run, or returns null when it is absent, already claimed by
    /// someone else, or no longer queued.
    /// <para>
    /// Used when execution is triggered per run — a serverless job invoked with a run id
    /// rather than a long-lived worker draining a queue. Still goes through SKIP LOCKED, so a
    /// duplicate trigger for the same run claims nothing instead of running it twice.
    /// </para>
    /// </summary>
    Task<PipelineRun?> TryClaimAsync(Guid runId, string workerId, CancellationToken ct = default);

    /// <summary>
    /// Takes up to <paramref name="batchSize"/> queued runs that no job has been started for
    /// recently, stamps them as dispatched, and returns their ids.
    /// </summary>
    /// <remarks>
    /// The third claim in this class, and the same mechanism as the other two, because the
    /// problem is the same: several API instances sweep this table and must not all start a
    /// job for the same run. SKIP LOCKED makes them share the work rather than race for it.
    /// <para>
    /// It also picks up runs whose worker died — <see cref="RunStatus.Running"/> with a lease
    /// that has expired. A run in that state is a queued run that does not know it yet, and
    /// treating it as one collapses two recovery mechanisms into a predicate: there is no
    /// separate reaper, no cron to remember, and a dead executor costs seconds rather than
    /// however long that cron's interval was. Attempts are still bounded — a run that has
    /// exhausted them fails here rather than being handed out again.
    /// </para>
    /// <para>
    /// A dispatch does not change <see cref="RunStatus"/> for a run that was already queued.
    /// The job may fail to boot, and the only thing that may say a run is running is a worker
    /// that has actually taken it. <paramref name="redispatchAfter"/> is what lets a run that
    /// was dispatched into silence be tried again.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<Guid>> ClaimForDispatchAsync(
        int batchSize,
        TimeSpan redispatchAfter,
        CancellationToken ct = default);

    /// <summary>
    /// Undoes the stamp on runs a job could not be started for, so the next sweep retries them
    /// immediately rather than after the redispatch window.
    /// </summary>
    Task ReleaseDispatchAsync(IReadOnlyCollection<Guid> runIds, CancellationToken ct = default);

    /// <summary>
    /// Records that starting a job for this run was refused in a way retrying will not fix,
    /// and fails the run once that has happened enough times. Returns whether it is still
    /// worth retrying.
    /// </summary>
    Task<bool> RecordDispatchRefusalAsync(Guid runId, string reason, CancellationToken ct = default);

    /// <summary>How many runs are queued, and how long the oldest has been waiting.</summary>
    /// <remarks>
    /// The one number that would have shown the trigger was missing: with nothing reading the
    /// queue, this grew without bound and nothing said so.
    /// </remarks>
    Task<(int Count, TimeSpan OldestAge)> QueueDepthAsync(CancellationToken ct = default);

}

/// <summary>
/// A queue built on the runs table itself.
/// <para>
/// Postgres <c>FOR UPDATE SKIP LOCKED</c> is a genuine work queue: concurrent workers each
/// walk past rows their peers have locked instead of blocking or colliding. Starting here
/// rather than with RabbitMQ keeps one less piece of infrastructure to operate, and the
/// consumer contract below is narrow enough to swap later if throughput ever demands it.
/// </para>
/// </summary>
public sealed class PipelineRunQueue(SpaceCraftDbContext db, ILogger<PipelineRunQueue> logger) : IPipelineRunQueue
{
    public async Task<PipelineRun?> TryClaimAsync(string workerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        // Connection retries are enabled, and a retrying execution strategy refuses to run
        // alongside a hand-rolled transaction — it has no way to replay one. Handing it the
        // whole claim makes the transaction part of the retriable unit.
        var strategy = db.Database.CreateExecutionStrategy();

        var run = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // No LINQ operators may be composed onto this query: EF would wrap it in a
            // subquery, and Postgres rejects FOR UPDATE inside one.
            //
            // xmin by name, here and in the two claims below: it is the run's concurrency
            // token, and a system column that SELECT * leaves out. Without it EF refuses the
            // rows for missing a mapped column.
            var candidates = await db.PipelineRuns
                .FromSql($"""
                    SELECT *, xmin FROM spacecraft.pipeline_runs
                    WHERE status = {(int)RunStatus.Queued}
                    ORDER BY queued_at
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(ct);

            var candidate = candidates.FirstOrDefault();
            if (candidate is null)
            {
                await transaction.RollbackAsync(ct);
                return null;
            }

            candidate.Claim(workerId, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return candidate;
        });

        if (run is null)
        {
            return null;
        }

        // Steps are loaded separately: FromSql cannot carry an Include.
        await db.Entry(run).Collection(r => r.Steps).LoadAsync(ct);

        Log.Claimed(logger, workerId, run.Id, run.AttemptCount);

        return run;
    }

    public async Task<PipelineRun?> TryClaimAsync(Guid runId, string workerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        var strategy = db.Database.CreateExecutionStrategy();

        var run = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var candidates = await db.PipelineRuns
                .FromSql($"""
                    SELECT *, xmin FROM spacecraft.pipeline_runs
                    WHERE id = {runId} AND status = {(int)RunStatus.Queued}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(ct);

            var candidate = candidates.FirstOrDefault();
            if (candidate is null)
            {
                await transaction.RollbackAsync(ct);
                return null;
            }

            candidate.Claim(workerId, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return candidate;
        });

        if (run is null)
        {
            Log.NotClaimable(logger, runId, workerId);
            return null;
        }

        await db.Entry(run).Collection(r => r.Steps).LoadAsync(ct);

        Log.ClaimedById(logger, workerId, run.Id, run.AttemptCount);

        return run;
    }

    public async Task<IReadOnlyList<Guid>> ClaimForDispatchAsync(
        int batchSize,
        TimeSpan redispatchAfter,
        CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var now = DateTimeOffset.UtcNow;
            var cutoff = now - redispatchAfter;

            // Two shapes, one query: a run waiting for its first job, and a run whose worker
            // died holding it. Oldest first, so a backlog drains in the order people queued it
            // — with id as a tiebreak, because two runs queued in the same millisecond share a
            // queued_at and would otherwise come back in whatever order the index felt like.
            // Ids are UUIDv7, so the tiebreak is itself time-ordered rather than arbitrary.
            // SELECT *, xmin because FromSql must return every mapped column — the token
            // included — and because these rows are tracked and mutated below rather than
            // projected.
            var claimed = await db.PipelineRuns
                .FromSql($"""
                    SELECT *, xmin FROM spacecraft.pipeline_runs
                    WHERE (status = {(int)RunStatus.Queued}
                           AND (dispatched_at IS NULL OR dispatched_at < {cutoff}))
                       OR (status = {(int)RunStatus.Running}
                           AND lease_expires_at IS NOT NULL
                           AND lease_expires_at < {now})
                    ORDER BY queued_at, id
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .Include(r => r.Steps)
                // Ordered again out here, and this is not redundant. Include makes EF wrap the
                // statement above as a subquery and join the steps onto it, and an ORDER BY
                // inside a subquery does not survive that — the rows come back in whatever
                // order the join produces. The inner ORDER BY still decides *which* rows the
                // LIMIT takes; this decides the order they are handed back in.
                .OrderBy(r => r.QueuedAt)
                .ThenBy(r => r.Id)
                .ToListAsync(ct);

            if (claimed.Count == 0)
            {
                await transaction.RollbackAsync(ct);
                return (IReadOnlyList<Guid>)[];
            }

            var dispatchable = new List<Guid>(claimed.Count);

            foreach (var run in claimed)
            {
                if (run.Status is RunStatus.Running)
                {
                    // Its lease lapsed, so whatever held it is gone. Back to the queue, unless
                    // it has used its attempts — in which case it fails here and is not handed
                    // out again. Steps left mid-flight are failed with the same reason, which
                    // is why this query loads them.
                    var requeued = run.TryRequeue(
                        "Worker lease expired; the worker likely crashed or was evicted.");

                    if (requeued)
                    {
                        Log.LeaseExpiredRequeued(logger, run.Id, run.AttemptCount);
                    }
                    else
                    {
                        Log.LeaseExpiredGaveUp(logger, run.Id, run.AttemptCount);
                        continue;
                    }
                }

                run.MarkDispatched(now);
                dispatchable.Add(run.Id);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return (IReadOnlyList<Guid>)dispatchable;
        });
    }

    public async Task ReleaseDispatchAsync(IReadOnlyCollection<Guid> runIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runIds);

        if (runIds.Count == 0)
        {
            return;
        }

        await db.PipelineRuns
            .Where(r => runIds.Contains(r.Id) && r.Status == RunStatus.Queued)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.DispatchedAt, (DateTimeOffset?)null), ct);
    }

    public async Task<bool> RecordDispatchRefusalAsync(
        Guid runId, string reason, CancellationToken ct = default)
    {
        // With its steps: the refusal that reaches the limit fails the run, and Fail settles
        // the steps still Pending. Loaded without them it would settle nothing, and the run
        // would end Failed over four steps that never resolve.
        var run = await db.PipelineRuns
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);

        if (run is null || run.Status != RunStatus.Queued)
        {
            return false;
        }

        var retryable = run.TryRecordDispatchRefusal(reason);
        await db.SaveChangesAsync(ct);

        if (!retryable)
        {
            Log.DispatchGaveUp(logger, runId, run.DispatchAttempts);
        }

        return retryable;
    }

    public async Task<(int Count, TimeSpan OldestAge)> QueueDepthAsync(CancellationToken ct = default)
    {
        var oldest = await db.PipelineRuns
            .Where(r => r.Status == RunStatus.Queued)
            .OrderBy(r => r.QueuedAt)
            .Select(r => (DateTimeOffset?)r.QueuedAt)
            .FirstOrDefaultAsync(ct);

        var count = await db.PipelineRuns.CountAsync(r => r.Status == RunStatus.Queued, ct);

        return (count, oldest is null ? TimeSpan.Zero : DateTimeOffset.UtcNow - oldest.Value);
    }

}

/// <summary>
/// Source-generated logging for the queue. These fire on every claim, so the generated
/// methods keep the arguments from being marshalled when the level is disabled.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Worker {WorkerId} claimed run {RunId} (attempt {Attempt})")]
    public static partial void Claimed(ILogger logger, string workerId, Guid runId, int attempt);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Worker {WorkerId} claimed run {RunId} by id (attempt {Attempt})")]
    public static partial void ClaimedById(ILogger logger, string workerId, Guid runId, int attempt);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Run {RunId} was not claimable by {WorkerId}: absent, already running, or finished")]
    public static partial void NotClaimable(ILogger logger, Guid runId, string workerId);

    // Error, not Warning: a lease only lapses because whatever held the run died — an OOM, an
    // eviction, a job killed at its timeout. None of those is a condition to watch pass by, and
    // an error reporter is the right place for them to land.
    //
    // Two templates rather than one with a variable, because a reporter groups by template.
    // "recovered" and "gave up" are different incidents and should not share a bucket.
    [LoggerMessage(Level = LogLevel.Error,
        Message = "Run {RunId} lease expired after {Attempts} attempt(s) and was requeued; its worker died mid-run")]
    public static partial void LeaseExpiredRequeued(ILogger logger, Guid runId, int attempts);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Run {RunId} failed: its worker died mid-run for the {Attempts}th time, which is the limit")]
    public static partial void LeaseExpiredGaveUp(ILogger logger, Guid runId, int attempts);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Run {RunId} failed: a job could not be started for it after {Attempts} attempt(s)")]
    public static partial void DispatchGaveUp(ILogger logger, Guid runId, int attempts);
}
