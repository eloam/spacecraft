using Microsoft.EntityFrameworkCore;
using SpaceCraft.Infrastructure.Persistence;

namespace SpaceCraft.Api.Common;

/// <summary>
/// Keeps the notification history from being kept for ever.
/// </summary>
/// <remarks>
/// Not about disk. The count beside the bell is the query this application runs most often —
/// every open tab, every minute — and it counts a person's unread rows; the list beside it
/// reads their newest. Both stay cheap while the history is bounded and neither does once it
/// is not, so the purge is what keeps a two-year-old account as fast as a two-day-old one.
/// <para>
/// Read and unread are aged differently on purpose. Something already read has done its work
/// and can go early. Something unread is still owed to somebody, so it is given far longer —
/// but not for ever, because a notification nobody opened in three months is not going to be
/// opened, and keeping it makes every later one slower.
/// </para>
/// <para>
/// Deliveries go with their notification: the foreign key cascades, so this deletes one table
/// and Postgres clears the other.
/// </para>
/// </remarks>
public sealed class NotificationRetention(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<NotificationRetention> logger) : BackgroundService
{
    /// <summary>Rows per statement, so a purge never takes a lock worth noticing.</summary>
    private const int BatchSize = 500;

    /// <summary>
    /// Batches per sweep. A bound rather than "until it is empty": the first run against a
    /// history that has been accumulating should not become the longest transaction of the
    /// day. What it does not reach, the next sweep does.
    /// </summary>
    private const int MaxBatches = 20;

    private readonly TimeSpan _interval = TimeSpan.FromHours(
        Math.Clamp(configuration.GetValue<int?>("Notifications:PurgeHours") ?? 6, 1, 168));

    private readonly TimeSpan _readRetention = TimeSpan.FromDays(
        Math.Clamp(configuration.GetValue<int?>("Notifications:ReadRetentionDays") ?? 30, 1, 3650));

    private readonly TimeSpan _unreadRetention = TimeSpan.FromDays(
        Math.Clamp(configuration.GetValue<int?>("Notifications:UnreadRetentionDays") ?? 90, 1, 3650));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Purging old notifications failed");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var now = DateTimeOffset.UtcNow;
        var readBefore = now - _readRetention;
        var anyBefore = now - _unreadRetention;

        var total = 0;

        for (var batch = 0; batch < MaxBatches; batch++)
        {
            var doomed = db.Notifications
                .Where(n => (n.ReadAt != null && n.CreatedAt < readBefore) || n.CreatedAt < anyBefore)
                .OrderBy(n => n.CreatedAt)
                .Take(BatchSize)
                .Select(n => n.Id);

            var removed = await db.Notifications
                .Where(n => doomed.Contains(n.Id))
                .ExecuteDeleteAsync(ct);

            total += removed;

            if (removed < BatchSize)
            {
                break;
            }

            if (batch == MaxBatches - 1)
            {
                // Said out loud, because otherwise a purge that never catches up looks exactly
                // like one with nothing to do.
                logger.LogWarning(
                    "Notification purge stopped at its batch limit with more still to remove");
            }
        }

        if (total > 0)
        {
            logger.LogInformation("Purged {Count} notification(s) past their retention", total);
        }
    }
}
