using SpaceCraft.Api.Common;

namespace SpaceCraft.Api.Email;

/// <summary>
/// Accepts a message and returns immediately, leaving delivery to <see cref="EmailDispatcher"/>.
/// See <see cref="BackgroundWorkQueue{T}"/> for why sending inline is not an option.
/// </summary>
public sealed class QueuedEmailSender(BackgroundWorkQueue<EmailMessage> queue) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        queue.Enqueue(message);
        return Task.CompletedTask;
    }
}

/// <summary>Drains the queue and hands each message to the configured transport.</summary>
public sealed class EmailDispatcher(
    BackgroundWorkQueue<EmailMessage> queue,
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
