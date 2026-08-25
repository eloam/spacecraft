using System.Threading.Channels;

namespace SpaceCraft.Api.Email;

/// <summary>
/// Queue of "someone asked to reset the password for this address" requests.
/// <para>
/// The endpoint does no work at all beyond enqueuing, and that is the point. Answering
/// identically for a registered and an unregistered address is only half the defence: if the
/// registered branch also looks up the user and writes a token while the unregistered branch
/// returns immediately, the difference is plainly visible in the response time. Measured
/// before this queue existed, the two branches were 21.5 ms and 4.6 ms — a reliable account
/// oracle despite identical responses.
/// </para>
/// <para>
/// Nothing here is observable to the caller, so nothing here can leak. Requests are dropped
/// on saturation and lost if the process dies; a user who receives no email asks again.
/// </para>
/// </summary>
public sealed class PasswordResetRequests(ILogger<PasswordResetRequests> logger)
{
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(
        new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    public ChannelReader<string> Reader => _queue.Reader;

    public void Enqueue(string emailAddress)
    {
        if (!_queue.Writer.TryWrite(emailAddress))
        {
            // Deliberately does not name the address: this log line would otherwise become a
            // record of who has been asking for resets.
            logger.LogError("Password reset queue is full; a request was dropped.");
        }
    }
}
