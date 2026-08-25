using System.Threading.Channels;

namespace SpaceCraft.Api.Email;

/// <summary>
/// A bounded in-memory queue for work that must not happen on the request thread.
/// <para>
/// Used where doing the work inline would leak information through response time. An endpoint
/// that answers identically for two cases still answers differently if one case reads the
/// database and the other returns immediately, and that difference is measurable. Enqueuing
/// unconditionally is what makes the two indistinguishable.
/// </para>
/// <para>
/// Bounded on purpose: an unbounded queue turns a downstream outage into memory exhaustion.
/// Under saturation the newest item is dropped, and items in flight are lost if the process
/// stops — acceptable for work a user can simply ask for again, not for anything that must
/// survive a restart.
/// </para>
/// </summary>
public sealed class BackgroundWorkQueue<T>(ILogger<BackgroundWorkQueue<T>> logger)
{
    private readonly Channel<T> _channel = Channel.CreateBounded<T>(
        new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    public ChannelReader<T> Reader => _channel.Reader;

    public void Enqueue(T item)
    {
        if (!_channel.Writer.TryWrite(item))
        {
            // No item detail in the message: for these queues the payload is an email
            // address, and this log must not become a record of who has been asking.
            logger.LogError("{Queue} is full; an item was dropped.", typeof(T).Name);
        }
    }
}
