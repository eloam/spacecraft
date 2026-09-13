using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Notifications;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Api.Email;

/// <summary>
/// Turns notification deliveries into email.
/// </summary>
/// <remarks>
/// A sweep of a table rather than an in-memory queue like the other dispatchers, because this
/// one has a producer it cannot hear: runs finish in the runner, a separate process, and the
/// notification it writes has to reach a mailbox from here. Polling a table both processes
/// can write is the only thing they share.
/// <para>
/// It sweeps only its own channel. The next transport claims its own rows from the same
/// table, on the same terms, and neither has to know the other exists — which is the point of
/// a delivery being a row rather than three more columns on the notification.
/// </para>
/// <para>
/// A batch is claimed with <c>FOR UPDATE SKIP LOCKED</c>, the same way the run queue claims
/// work, and stamped as sent before anything is handed over. Two things follow. Several API
/// instances sweep past each other's rows instead of racing for the same fifty. And a crash
/// between the claim and the send loses a message rather than sending it twice, which is the
/// right way round for something that lands in somebody's inbox.
/// </para>
/// </remarks>
public sealed class NotificationDispatcher(
    IServiceScopeFactory scopeFactory,
    IEmailSender email,
    IConfiguration configuration,
    IOptions<PublicUrlsOptions> publicUrls,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    /// <summary>
    /// Long enough that an idle deployment is not querying constantly, short enough that a
    /// failed run reaches a mailbox while the person who started it still remembers it.
    /// </summary>
    /// <remarks>
    /// Configurable because the tests assert that a message actually arrives, and waiting a
    /// quarter of a minute for each is a suite nobody runs. Clamped so the setting cannot
    /// turn the sweep into a busy loop against the database.
    /// </remarks>
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(
        Math.Clamp(configuration.GetValue<int?>("Notifications:SweepSeconds") ?? 15, 1, 300));

    /// <summary>
    /// How many are claimed per sweep. With the interval, this is the ceiling on delivery
    /// rate — fifty every fifteen seconds is two hundred a minute — so it is the first thing
    /// to raise if a backlog ever builds, and a setting rather than a constant for that reason.
    /// </summary>
    /// <remarks>
    /// It is also how much a crash costs. A batch is stamped as sent before any of it is
    /// handed over, so a process that dies mid-sweep loses what it was holding: at most this
    /// many messages, never a duplicate. Raising it for throughput widens that window in the
    /// same breath.
    /// </remarks>
    private readonly int _batchSize =
        Math.Clamp(configuration.GetValue<int?>("Notifications:BatchSize") ?? 50, 1, 500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The sweep failing must not end the service: the next tick tries again.
                logger.LogError(ex, "Sweeping notifications for delivery failed");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        foreach (var delivery in await ClaimAsync(db, ct))
        {
            try
            {
                await DeliverAsync(db, delivery, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Delivering already handles its own failures; this catches the failure of
                // that handling — a database that went away while releasing a row, say.
                // Without it one bad row ends the loop, and everything claimed behind it stays
                // claimed for ever: marked sent, never sent.
                logger.LogError(ex, "Notification delivery {DeliveryId} could not be settled", delivery.Id);
            }
        }
    }

    /// <summary>
    /// Takes a batch, skipping whatever another instance is already holding.
    /// </summary>
    private async Task<List<NotificationDelivery>> ClaimAsync(
        SpaceCraftDbContext db, CancellationToken ct)
    {
        // Connection retries are enabled, and a retrying execution strategy refuses to run
        // alongside a hand-rolled transaction — it has no way to replay one. Handing it the
        // whole claim makes the transaction part of the retriable unit.
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // No LINQ operators may be composed onto this query: EF would wrap it in a
            // subquery, and Postgres rejects FOR UPDATE inside one.
            var due = await db.NotificationDeliveries
                .FromSql($"""
                    SELECT * FROM spacecraft.notification_deliveries
                    WHERE channel = {(int)NotificationChannel.Email}
                      AND sent_at IS NULL
                      AND attempts < {NotificationDelivery.MaxAttempts}
                    ORDER BY created_at
                    LIMIT {_batchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(ct);

            if (due.Count == 0)
            {
                await transaction.RollbackAsync(ct);
                return due;
            }

            var now = DateTimeOffset.UtcNow;

            foreach (var delivery in due)
            {
                delivery.Claim(now);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return due;
        });
    }

    private async Task DeliverAsync(
        SpaceCraftDbContext db, NotificationDelivery delivery, CancellationToken ct)
    {
        try
        {
            var message = await ComposeAsync(db, delivery, ct);

            if (message is not null)
            {
                await email.SendAsync(message, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            delivery.Release();
            await db.SaveChangesAsync(ct);

            if (delivery.IsAbandoned)
            {
                // Said out loud, once. Otherwise a message that will never be sent leaves no
                // trace anywhere: the row simply stops matching the sweep, and the person it
                // was for is never told and never knows they were not.
                logger.LogWarning(
                    ex,
                    "Giving up on notification delivery {DeliveryId} after {Attempts} attempts",
                    delivery.Id, delivery.Attempts);
            }
            else
            {
                logger.LogError(ex, "Delivering notification {DeliveryId} failed", delivery.Id);
            }
        }
    }

    /// <summary>
    /// Builds the message for one delivery, or nothing if there is nobody left to send it to.
    /// </summary>
    private async Task<EmailMessage?> ComposeAsync(
        SpaceCraftDbContext db, NotificationDelivery delivery, CancellationToken ct)
    {
        var row = await (
            from notification in db.Notifications.AsNoTracking()
            join user in db.Users.AsNoTracking() on notification.UserId equals user.Id
            join organization in db.Organizations.AsNoTracking()
                on notification.OrganizationId equals organization.Id
            where notification.Id == delivery.NotificationId
            select new
            {
                notification.Title,
                notification.Summary,
                notification.Detail,
                notification.Link,
                notification.ActionLabel,
                user.Email,
                OrganizationName = organization.Name,
            }).FirstOrDefaultAsync(ct);

        if (row?.Email is null)
        {
            // The account or the organization went away between the event and this sweep.
            return null;
        }

        var urls = publicUrls.Value;

        return Emails.Notification(
            row.Email,
            row.OrganizationName,
            row.Title,
            row.Summary,
            Shorten(row.Detail),
            row.ActionLabel,
            urls.AppRoot() + row.Link,
            urls.AppRoot() + "/account/notifications",
            urls.ApiRoot());
    }

    /// <summary>
    /// What an email carries of the detail: the first line, and not much of it.
    /// </summary>
    /// <remarks>
    /// A failed run's detail ends in the tail of the build output. That is worth reading, and
    /// it stays where it was — in the application, behind a session and a role. An email goes
    /// to a mailbox, which may be an alias, a forward or a personal account; putting build
    /// output there widens who can read it to buy what the link already gives.
    /// </remarks>
    private static string? Shorten(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        var firstLine = detail.Split('\n')[0].Trim();

        return firstLine.Length <= 240 ? firstLine : firstLine[..240] + "…";
    }
}
