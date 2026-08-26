using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Pipeline.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Runner;

/// <summary>Outcome of one invocation, mapped to a process exit code by the entry point.</summary>
public enum RunnerOutcome
{
    /// <summary>The work asked for was carried out. A run that failed on its own merits counts.</summary>
    Completed = 0,

    /// <summary>Nothing was there to do. Not an error: a trigger can always lose a race.</summary>
    NothingToDo = 1,

    /// <summary>The runner could not do its job — configuration, database, or an unhandled fault.</summary>
    Faulted = 2,
}

/// <summary>
/// Executes pipeline runs and then stops, rather than servicing a queue forever.
/// <para>
/// This is the same work <c>SpaceCraft.Worker</c> does, shaped for an environment where the
/// process *is* the unit of scheduling — a serverless job, a batch task, a one-shot pod. The
/// long-lived worker keeps its polling loop; both call the same executor underneath, so the
/// pipeline has exactly one implementation.
/// </para>
/// <para>
/// A run that fails is a normal result, not a failure of the invocation: the run is marked
/// failed in the database and the process still exits zero. Exiting non-zero would make a
/// scheduler retry the whole job and re-execute work that is already accounted for.
/// </para>
/// </summary>
public sealed class RunExecutor(
    IServiceScopeFactory scopeFactory,
    IOptions<RunnerOptions> options,
    TimeProvider time,
    ILogger<RunExecutor> logger)
{
    private readonly RunnerOptions _options = options.Value;

    public async Task<RunnerOutcome> ExecuteAsync(RunnerCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Mode switch
        {
            RunnerMode.OneRun => await ExecuteSingleAsync(command.RunId!.Value, ct),
            RunnerMode.Drain => await DrainAsync(ct),
            RunnerMode.Reap => await ReapAsync(ct),
            RunnerMode.Migrate => await MigrateAsync(ct),
            _ => RunnerOutcome.Faulted,
        };
    }

    private async Task<RunnerOutcome> ExecuteSingleAsync(Guid runId, CancellationToken ct)
    {
        var executorId = ExecutorId(slot: 0);

        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>();

        var run = await queue.TryClaimAsync(runId, executorId, ct);
        if (run is null)
        {
            // Another executor got there first, or the run was cancelled between trigger and
            // start. Both are ordinary, so this is not an error.
            logger.LogInformation("Run {RunId} was not available to claim; nothing to do", runId);
            return RunnerOutcome.NothingToDo;
        }

        var executor = scope.ServiceProvider.GetRequiredService<IPipelineExecutor>();
        await executor.ExecuteAsync(run.Id, ct);

        return RunnerOutcome.Completed;
    }

    private async Task<RunnerOutcome> DrainAsync(CancellationToken ct)
    {
        var concurrency = Math.Max(1, _options.Concurrency);
        var budget = new DrainBudget(_options.MaxRunsPerInvocation, _options.MaxDuration, time);

        logger.LogInformation(
            "Draining the queue: concurrency {Concurrency}, at most {MaxRuns} run(s), budget {Budget}",
            concurrency, _options.MaxRunsPerInvocation, _options.MaxDuration);

        await Task.WhenAll(Enumerable
            .Range(0, concurrency)
            .Select(slot => DrainSlotAsync(slot, budget, ct)));

        logger.LogInformation(
            "Drain finished after {Count} run(s) in {Elapsed}", budget.Executed, budget.Elapsed);

        return budget.Executed > 0 ? RunnerOutcome.Completed : RunnerOutcome.NothingToDo;
    }

    private async Task DrainSlotAsync(int slot, DrainBudget budget, CancellationToken ct)
    {
        var executorId = ExecutorId(slot);
        var graceRemaining = _options.EmptyQueueGrace;

        while (!ct.IsCancellationRequested)
        {
            // Reserve capacity before claiming, so concurrent slots cannot collectively
            // overrun the budget by one run each.
            if (!budget.TryReserve(out var reason))
            {
                logger.LogInformation("Executor {ExecutorId} stopping: {Reason}", executorId, reason);
                return;
            }

            // A scope per run: a DbContext must not be shared across concurrent runs, and a
            // long build should not pin a connection for the whole pipeline.
            await using var scope = scopeFactory.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>();

            var run = await queue.TryClaimAsync(executorId, ct);

            if (run is null)
            {
                budget.Release();

                if (graceRemaining <= TimeSpan.Zero)
                {
                    return;
                }

                // Give a just-committed run a moment to appear, rather than exiting on a race
                // between the trigger firing and the transaction that queued the run.
                await Task.Delay(graceRemaining, time, ct);
                graceRemaining = TimeSpan.Zero;
                continue;
            }

            graceRemaining = _options.EmptyQueueGrace;

            var executor = scope.ServiceProvider.GetRequiredService<IPipelineExecutor>();
            await executor.ExecuteAsync(run.Id, ct);
        }
    }

    /// <summary>
    /// Shared ceiling on how much one drain invocation may do. Reservations are taken before
    /// a claim and handed back when the queue turns out to be empty, so the count tracks work
    /// actually performed.
    /// </summary>
    private sealed class DrainBudget(int maxRuns, TimeSpan maxDuration, TimeProvider time)
    {
        private readonly long _startedAt = time.GetTimestamp();
        private int _reserved;

        public int Executed => Volatile.Read(ref _reserved);

        public TimeSpan Elapsed => time.GetElapsedTime(_startedAt);

        public bool TryReserve(out string reason)
        {
            if (Elapsed >= maxDuration)
            {
                reason = "time budget spent";
                return false;
            }

            if (Interlocked.Increment(ref _reserved) > maxRuns)
            {
                Interlocked.Decrement(ref _reserved);
                reason = "run budget spent";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public void Release() => Interlocked.Decrement(ref _reserved);
    }

    private async Task<RunnerOutcome> ReapAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>();

        var requeued = await queue.RequeueExpiredAsync(ct);

        if (requeued == 0)
        {
            logger.LogInformation("No abandoned runs to recover");
            return RunnerOutcome.NothingToDo;
        }

        logger.LogWarning("Recovered {Count} abandoned run(s)", requeued);
        return RunnerOutcome.Completed;
    }

    /// <summary>
    /// Applies pending migrations, as a deployment step rather than at application startup.
    /// <para>
    /// Startup migration is convenient with one instance and dangerous with several: two
    /// containers coming up together race on the same migration. Running it as its own job,
    /// to completion, before the new revision is rolled out, removes the race entirely.
    /// </para>
    /// </summary>
    private async Task<RunnerOutcome> MigrateAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("Database is already up to date");
            return RunnerOutcome.NothingToDo;
        }

        logger.LogInformation("Applying {Count} migration(s): {Migrations}",
            pending.Count, string.Join(", ", pending));

        await db.Database.MigrateAsync(ct);

        logger.LogInformation("Migrations applied");
        return RunnerOutcome.Completed;
    }

    /// <summary>
    /// Identifies the claimant in the runs table. Includes the machine so an abandoned lease
    /// can be traced back to the invocation that took it.
    /// </summary>
    private static string ExecutorId(int slot) =>
        $"runner-{Environment.MachineName}-{Environment.ProcessId}-{slot}";
}
