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
/// It runs here rather than beside <c>--reap</c> in the runner image because the key that
/// starts jobs must not travel to a process that executes a customer's build. That is the
/// whole reason <see cref="PipelineJobOptions"/> lives in this project.
/// </para>
/// <para>
/// One execution per run, never a drain: the isolation this buys is per container, and a job
/// that took several runs would spend it — one tenant's <c>npm install</c> in the same process
/// as another's publish token.
/// </para>
/// </remarks>
public sealed class RunDispatcher(
    IServiceScopeFactory scopeFactory,
    IJobTrigger trigger,
    PipelineMetrics metrics,
    IOptions<PipelineJobOptions> options,
    ILogger<RunDispatcher> logger) : BackgroundService
{
    private readonly PipelineJobOptions _options = options.Value;


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(_options.SweepSeconds, 1, 300));
        using var timer = new PeriodicTimer(interval);

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

    private async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>();

        // The claim belongs to the queue, not to this class. Several API instances sweep the
        // same table, and SKIP LOCKED is what makes them share the work instead of each
        // starting a job for the same run — three executions spent against a ceiling of ten.
        var claimed = await queue.ClaimForDispatchAsync(
            Math.Clamp(_options.BatchSize, 1, 100),
            TimeSpan.FromSeconds(Math.Clamp(_options.RedispatchAfterSeconds, 10, 3600)),
            ct);

        if (claimed.Count == 0)
        {
            return;
        }

        var unstarted = new List<Guid>();

        for (var i = 0; i < claimed.Count; i++)
        {
            var runId = claimed[i];
            var result = await trigger.StartAsync(runId, ct);

            if (result.Outcome is JobStartOutcome.Started)
            {
                metrics.JobsStarted.Add(1);
                logger.LogInformation("Started job {JobRunId} for run {RunId}", result.JobRunId, runId);
                continue;
            }

            // Nothing was started, so the stamp has to come off or the run would wait out the
            // whole redispatch window for a job that never existed.
            unstarted.Add(runId);

            if (result.Outcome is JobStartOutcome.Deferred)
            {
                // The parallel-run ceiling, or the platform being unreachable. Both apply to
                // the rest of the batch too, so stop asking and release what is left.
                metrics.JobsDeferred.Add(1);
                logger.LogDebug("Deferred run {RunId}: {Detail}", runId, result.Detail);
                unstarted.AddRange(claimed.Skip(i + 1));
                break;
            }

            // A deployment fault — an unknown job definition, a key that is not accepted.
            // Recoverable the moment somebody fixes the setting, so it is retried; but not for
            // ever, because a run nobody can start has to say so rather than spin in silence.
            // Counted here rather than released above: the refusal itself clears the stamp.
            metrics.JobsRefused.Add(1);
            unstarted.Remove(runId);
            logger.LogError("Could not start a job for run {RunId}: {Detail}", runId, result.Detail);
            await queue.RecordDispatchRefusalAsync(runId, result.Detail ?? "No detail.", ct);
        }

        await queue.ReleaseDispatchAsync(unstarted, ct);
    }
}
