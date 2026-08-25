using System.Threading.Channels;
using SpaceCraft.Api.Common;

namespace SpaceCraft.Api.Email;

/// <summary>
/// Accepts a message and returns immediately, leaving delivery to a background dispatcher.
/// <para>
/// This is a security control, not just a latency optimisation. <c>forgot-password</c>
/// answers identically for a registered and an unregistered address, but only if it also
/// takes the same time: sending inline would add a provider round trip on exactly the
/// branch where the account exists, and timing that difference re-creates the account
/// oracle the identical responses were there to prevent.
/// </para>
/// <para>
/// The queue is in memory, so a process that dies with messages pending drops them. For a
/// reset link that is recoverable — the user asks again — and it is the trade for not
/// putting a mail outbox in the request path. A durable outbox is the upgrade if delivery
/// ever has to be guaranteed.
/// </para>
/// </summary>
public sealed class QueuedEmailSender : IEmailSender
{
    // Bounded: an unbounded queue turns a provider outage into memory exhaustion. Dropping
    // the newest message under saturation is better than taking the process down with it.
    private readonly Channel<EmailMessage> _queue = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    private readonly ILogger<QueuedEmailSender> _logger;

    public QueuedEmailSender(ILogger<QueuedEmailSender> logger) => _logger = logger;

    public ChannelReader<EmailMessage> Reader => _queue.Reader;

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_queue.Writer.TryWrite(message))
        {
            _logger.LogError(
                "Email queue is full; dropped {Subject}. Delivery is falling behind.",
                message.Subject);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Drains the queue and hands each message to the configured transport.</summary>
public sealed class EmailDispatcher(
    QueuedEmailSender queue,
    IEmailTransport transport,
    ILogger<EmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await transport.SendAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One undeliverable message must not stop the dispatcher; the transport has
                // already logged the specifics.
                logger.LogError(ex, "Dispatching {Subject} failed", message.Subject);
            }
        }
    }
}
