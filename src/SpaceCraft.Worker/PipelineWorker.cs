using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Pipeline.Execution;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Worker;

/// <summary>
/// Pulls runs off the queue and executes them.
/// <para>
/// Polls rather than subscribes. Postgres LISTEN/NOTIFY would cut the few seconds of
/// latency between queueing and pickup, but it needs a dedicated connection per listener
/// and does not survive a reconnect without a catch-up query — which is this loop anyway.
/// Worth revisiting only if queue latency becomes a real complaint.
/// </para>
/// </summary>
public sealed class PipelineWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    IHostEnvironment environment,
    ILogger<PipelineWorker> logger) : BackgroundService
{
    private readonly WorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var concurrency = Math.Max(1, _options.Concurrency);

        logger.LogInformation("Pipeline worker starting with concurrency {Concurrency}", concurrency);

        var loops = Enumerable
            .Range(0, concurrency)
            .Select(slot => ConsumeAsync($"{environment.ApplicationName}-{Environment.MachineName}-{slot}", stoppingToken));

        await Task.WhenAll(loops);
    }

    private async Task ConsumeAsync(string workerId, CancellationToken ct)
    {
        var idleDelay = _options.PollInterval;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // A scope per run: the DbContext must not be shared across concurrent runs,
                // and a long build should not hold a connection open for the whole pipeline.
                await using var scope = scopeFactory.CreateAsyncScope();

                var queue = scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>();
                var run = await queue.TryClaimAsync(workerId, ct);

                if (run is null)
                {
                    // Back off gradually so an idle deployment is not hammering Postgres.
                    await Task.Delay(idleDelay, ct);
                    idleDelay = TimeSpan.FromMilliseconds(
                        Math.Min(idleDelay.TotalMilliseconds * 1.5, _options.MaxPollInterval.TotalMilliseconds));
                    continue;
                }

                idleDelay = _options.PollInterval;

                var executor = scope.ServiceProvider.GetRequiredService<IPipelineExecutor>();
                await executor.ExecuteAsync(run.Id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The loop must survive anything a single run can throw; the run itself has
                // already been marked failed, or will be requeued when its lease lapses.
                logger.LogError(ex, "Worker {WorkerId} hit an unexpected error; continuing", workerId);
                await Task.Delay(_options.PollInterval, ct);
            }
        }

        logger.LogInformation("Worker {WorkerId} stopped", workerId);
    }
}
