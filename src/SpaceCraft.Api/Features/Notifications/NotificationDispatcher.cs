using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Notifications;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using SpaceCraft.Api.Email;

namespace SpaceCraft.Api.Features.Notifications;

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
/// <para>
/// The send itself is awaited, and a transport that throws hands the row back for the next
/// sweep. This is the outbox shape of eShop's <c>IntegrationEventLogService</c>: mark in
/// progress, publish, and on failure mark it so — except that here the in-progress mark and
/// the published mark are the same stamp, because a message that reached the relay has no
/// further state to record. What the stamp on its own cannot survive is the process dying
/// mid-send. What the release costs is at-most-once: a transport that throws is not always
/// a transport that said no — a relay that took the message and then hung up, or a timeout
/// spent after the DATA was accepted, throws too — and that row is sent again. Delivery is
/// therefore at-least-once when the transport fails and at-most-once when the process does,
/// which is the trade the transport's own retries already make, and the one worth making for
/// a message nobody would otherwise miss.
/// </para>
/// </remarks>
public sealed class NotificationDispatcher(
    IServiceScopeFactory scopeFactory,
    IEmailTransport transport,
    IOptions<NotificationOptions> options,
    IOptions<PublicUrlsOptions> publicUrls,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    // Both bounded by [Range] on the options class and refused at startup when outside it;
    // the reasoning for each value is on the property.
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(options.Value.SweepSeconds);

    private readonly int _batchSize = options.Value.BatchSize;

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

        var claimed = await ClaimAsync(db, ct);
        var offered = 0;

        try
        {
            for (; offered < claimed.Count; offered++)
            {
                var delivery = claimed[offered];

                try
                {
                    await DeliverAsync(db, delivery, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A transport failure is reported and settled in Deliver; what reaches
                    // here is everything else — a message that could not be composed, whose
                    // row was put back, or a database that went away while writing a
                    // release, whose row was not. Without this one bad row ends the loop, and
                    // everything claimed behind it stays claimed for ever: marked sent, never
                    // sent.
                    logger.LogError(
                        ex, "Notification delivery {DeliveryId} failed outside the transport", delivery.Id);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A stop, not a crash — every rolling deploy is one, and with the sends awaited
            // one after the other a batch takes long enough for a stop to land inside it. The
            // rows behind the one in flight were never offered, so they go back for whoever
            // sweeps next. The one in flight keeps its stamp: from here a send interrupted
            // halfway looks the same as a crash, and it is lost rather than doubled.
            foreach (var delivery in claimed.Skip(offered + 1))
            {
                delivery.Release();
            }

            await db.SaveChangesAsync(CancellationToken.None);
            throw;
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

    /// <summary>
    /// Composes one claimed delivery, settles it with the transport, and writes what became
    /// of it.
    /// </summary>
    /// <remarks>
    /// A message that cannot be built was never offered, so its row goes back the way a
    /// refused one does, with the attempt counted; the failure itself is not the transport's
    /// to report and is rethrown for the sweep to. Without that release the row would keep
    /// the claim's stamp — marked sent, never sent — which is the defect awaiting the send was
    /// meant to end.
    /// <para>
    /// Public so the release can be tested against a context with no database behind it; the
    /// sweep is the only other caller.
    /// </para>
    /// </remarks>
    public async Task DeliverAsync(
        SpaceCraftDbContext db, NotificationDelivery delivery, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(delivery);

        EmailMessage? message;

        try
        {
            message = await ComposeAsync(db, delivery, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            delivery.Release();
            await db.SaveChangesAsync(ct);
            throw;
        }

        if (message is not null)
        {
            await SettleAsync(delivery, message, ct);
        }

        // Writes the release when there was one, and nothing when the transport accepted the
        // message: the claim already recorded that as sent, and a row with no change costs no
        // round trip.
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Hands one claimed delivery to the transport, and takes the claim back if the transport
    /// threw.
    /// </summary>
    /// <remarks>
    /// The transport, not the queue the endpoints use. The queue accepts a message and returns
    /// before anything is sent, and drops what it cannot hold — which suits a password reset,
    /// where the person is at the keyboard and can ask again, and not this, where nobody asked
    /// and so nobody notices a message that never came. Awaiting the send is what gives the
    /// row's attempt count a meaning: a relay that refuses puts the row back for the next
    /// sweep, and after <see cref="NotificationDelivery.MaxAttempts"/> refusals it is left
    /// alone. A transport that threw after the relay had taken the message is put back all the
    /// same; the class remarks say what that costs.
    /// <para>
    /// Public so the settlement can be tested without a database; the sweep is the only other
    /// caller.
    /// </para>
    /// </remarks>
    public async Task SettleAsync(
        NotificationDelivery delivery, EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            await transport.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            delivery.Release();

            // The message's key beside the delivery's: the transport has just logged the
            // failure under the former, and this is the line that says which row it was.
            if (delivery.IsAbandoned)
            {
                // Said out loud, once. Otherwise a message that will never be sent leaves no
                // trace anywhere: the row simply stops matching the sweep, and the person it
                // was for is never told and never knows they were not.
                logger.LogWarning(
                    ex,
                    "Giving up on notification delivery {DeliveryId} after {Attempts} attempts; the last was email {MessageId}",
                    delivery.Id, delivery.Attempts, message.Id);
            }
            else
            {
                logger.LogError(
                    ex, "Delivering notification {DeliveryId} as email {MessageId} failed",
                    delivery.Id, message.Id);
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

        return NotificationEmails.Notification(
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
