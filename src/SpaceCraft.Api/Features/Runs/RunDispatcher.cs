using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Infrastructure.Persistence;

namespace SpaceCraft.Api.Features.Runs;

/// <summary>
/// Starts a job for every queued run.
/// </summary>
/// <remarks>
/// The missing half of the queue. The API writes a row and returns; until this existed nothing
/// read it outside docker compose, so a deployed run sat at <c>Queued</c> for ever — no build,
/// no publish, no mail, and nothing that ages out to say so.
/// <para>
/// A sweep rather than a call from the request handler. Two paths would mean the inline one is
/// best-effort and the sweep is the guarantee, so the inline one earns nothing but a second
/// place for the same bug to hide. One path, retried on its own schedule, is both simpler and
/// the only version that is correct when the platform is briefly unavailable.
/// </para>
/// <para>
/// It runs here rather than in the runner image because the key that starts jobs must not
/// travel to a process that executes a customer's build. That is the whole reason
/// <see cref="PipelineJobOptions"/> lives in this project.
/// </para>
/// <para>
/// One execution per run, never a drain: the isolation this buys is per container, and a job
/// that took several runs would spend it — one tenant's <c>npm install</c> in the same process
/// as another's publish token.
/// </para>
/// </remarks>
public sealed class RunDispatcher(
    IServiceScopeFactory scopeFactory,
    PipelineMetrics metrics,
    IOptions<PipelineJobOptions> options,
    ILogger<RunDispatcher> logger) : BackgroundService
{
    private readonly PipelineJobOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.SweepSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A sweep that throws must not take the loop with it: the next one is the
                // recovery, and a dispatcher that has stopped is indistinguishable from the
                // absence this class exists to fix.
                logger.LogError(ex, "Run dispatch sweep failed");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    /// <summary>
    /// One pass: claim a batch, ask the platform for a job per run, and settle whatever was not
    /// started. Internal so the composition — started, deferred, rejected, released — is tested
    /// without a timer or a database.
    /// </summary>
    internal async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>();

        // Per sweep, like the queue, and not through the constructor: the trigger is a typed
        // HttpClient, which is transient so that the factory can rotate its handler. Captured
        // by a singleton it would keep the first handler for the life of the process, and
        // with it the DNS answer that handler resolved on the day the API started.
        var trigger = scope.ServiceProvider.GetRequiredService<IJobTrigger>();

        // The claim belongs to the queue, not to this class. Several API instances sweep the
        // same table, and SKIP LOCKED is what makes them share the work instead of each
        // starting a job for the same run — three executions spent against a ceiling of ten.
        var claimed = await queue.ClaimForDispatchAsync(
            _options.BatchSize,
            TimeSpan.FromSeconds(_options.RedispatchAfterSeconds),
            ct);

        if (claimed.Count == 0)
        {
            return;
        }

        // Everything from this index on was claimed and never started. Released in a finally,
        // because a trigger or a refusal record that throws mid-batch would otherwise leave the
        // rest stamped for the whole redispatch window, waiting on jobs that never existed.
        var reached = 0;
        Exception? failure = null;

        try
        {
            for (; reached < claimed.Count; reached++)
            {
                var runId = claimed[reached];
                var result = await trigger.StartAsync(runId, ct);

                if (result.Outcome is JobStartOutcome.Started)
                {
                    metrics.JobsStarted.Add(1);
                    logger.LogInformation("Started job {JobRunId} for run {RunId}", result.JobRunId, runId);
                    continue;
                }

                if (result.Outcome is JobStartOutcome.Deferred)
                {
                    // The parallel-run ceiling, or the platform being unreachable. Both apply to
                    // the rest of the batch too, so stop asking; this run and those behind it
                    // are released below.
                    metrics.JobsDeferred.Add(1);
                    logger.LogDebug("Deferred run {RunId}: {Detail}", runId, result.Detail);
                    break;
                }

                // A deployment fault — an unknown job definition, a key that is not accepted.
                // Recoverable the moment somebody fixes the setting, so it is retried; but not
                // for ever, because a run nobody can start has to say so rather than spin in
                // silence. The refusal itself clears the stamp, so the loop moves past it.
                metrics.JobsRefused.Add(1);
                logger.LogError("Could not start a job for run {RunId}: {Detail}", runId, result.Detail);
                await queue.RecordDispatchRefusalAsync(runId, result.Detail ?? "No detail.", ct);
            }
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            // Nothing was started for these, so the stamp has to come off or each would wait
            // out the whole redispatch window. Under the sweep's own token: a release that a
            // shutdown cuts short only costs those runs that window, which is what it is for.
            try
            {
                await queue.ReleaseDispatchAsync(claimed.Skip(reached).ToList(), ct);
            }
            catch (Exception ex) when (failure is not null && ex is not OperationCanceledException)
            {
                // Logged here rather than thrown: thrown from a finally it would replace the
                // exception already on its way out, and that one — the trigger or the refusal
                // record — is what stopped the batch. A release that fails the same way, on
                // the same scoped context, would otherwise be the only thing in the log.
                logger.LogError(ex, "Could not release {Count} claimed runs after the sweep failed", claimed.Count - reached);
            }
        }
    }
}
