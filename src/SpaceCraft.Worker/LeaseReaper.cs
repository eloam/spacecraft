using SpaceCraft.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Worker;

/// <summary>
/// Returns runs whose worker vanished mid-build back to the queue.
/// <para>
/// Without this, a pod evicted during a fifteen-minute npm install leaves its run stuck in
/// Running forever: no worker will claim it, and nothing will ever mark it failed.
/// </para>
/// </summary>
public sealed class LeaseReaper(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<LeaseReaper> logger) : BackgroundService
{
    private readonly WorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.ReaperInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var queue = scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>();

                var requeued = await queue.RequeueExpiredAsync(stoppingToken);
                if (requeued > 0)
                {
                    logger.LogWarning("Recovered {Count} abandoned run(s)", requeued);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lease reaper pass failed; will retry");
            }
        }
    }
}
