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
    /// It does not change <see cref="RunStatus"/>. A dispatched run is still queued — the job
    /// may fail to boot, and the only thing that may say a run is running is a worker that has
    /// actually taken it. <paramref name="redispatchAfter"/> is what lets a run that was
    /// dispatched into silence be tried again.
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
    /// Returns runs whose worker died mid-flight back to the queue. Called periodically.
    /// </summary>
    Task<int> RequeueExpiredAsync(CancellationToken ct = default);
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
            var candidates = await db.PipelineRuns
                .FromSql($"""
                    SELECT * FROM spacecraft.pipeline_runs
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
                    SELECT * FROM spacecraft.pipeline_runs
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

            var cutoff = DateTimeOffset.UtcNow - redispatchAfter;

            // Oldest first, so a backlog drains in the order people queued it. SELECT * because
            // FromSql must return every mapped column, and because the rows are tracked and
            // stamped below rather than projected.
            var claimed = await db.PipelineRuns
                .FromSql($"""
                    SELECT * FROM spacecraft.pipeline_runs
                    WHERE status = {(int)RunStatus.Queued}
                      AND (dispatched_at IS NULL OR dispatched_at < {cutoff})
                    ORDER BY queued_at
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(ct);

            if (claimed.Count == 0)
            {
                await transaction.RollbackAsync(ct);
                return (IReadOnlyList<Guid>)[];
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var run in claimed)
            {
                run.MarkDispatched(now);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return (IReadOnlyList<Guid>)[.. claimed.Select(r => r.Id)];
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

    public async Task<int> RequeueExpiredAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var expired = await db.PipelineRuns
            .Where(r => r.Status == RunStatus.Running
                        && r.LeaseExpiresAt != null
                        && r.LeaseExpiresAt < now)
            .Include(r => r.Steps)
            .ToListAsync(ct);

        if (expired.Count == 0)
        {
            return 0;
        }

        foreach (var run in expired)
        {
            var requeued = run.TryRequeue("Worker lease expired; the worker likely crashed or was evicted.");

            Log.LeaseExpired(logger, run.Id, run.AttemptCount, requeued ? "requeued" : "failed permanently");
        }

        await db.SaveChangesAsync(ct);
        return expired.Count;
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

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Run {RunId} lease expired after {Attempts} attempt(s); {Action}")]
    public static partial void LeaseExpired(ILogger logger, Guid runId, int attempts, string action);
}
