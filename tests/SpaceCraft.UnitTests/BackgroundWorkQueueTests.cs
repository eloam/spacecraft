using SpaceCraft.Api.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class BackgroundWorkQueueTests
{
    private static BackgroundWorkQueue<string> NewQueue() =>
        new(NullLogger<BackgroundWorkQueue<string>>.Instance);

    [Fact]
    public async Task Items_come_back_out_in_order()
    {
        var queue = NewQueue();
        queue.Enqueue("first");
        queue.Enqueue("second");

        (await queue.Reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBe("first");
        (await queue.Reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBe("second");
    }

    [Fact]
    public void Enqueuing_never_blocks_or_throws()
    {
        // The whole point is that the request thread hands work over and returns; a queue
        // that could block would put the latency straight back where it was removed from.
        var queue = NewQueue();

        for (var i = 0; i < 5_000; i++)
        {
            Should.NotThrow(() => queue.Enqueue($"item-{i}"));
        }
    }

    [Fact]
    public async Task Saturation_drops_the_newest_rather_than_growing_without_bound()
    {
        var queue = NewQueue();

        for (var i = 0; i < 1_500; i++)
        {
            queue.Enqueue($"item-{i}");
        }

        // Capacity is 1,000: everything past it is dropped, so the oldest survive and the
        // process does not accumulate a backlog until it runs out of memory.
        var drained = 0;
        while (queue.Reader.TryRead(out _))
        {
            drained++;
        }

        drained.ShouldBe(1_000);
        await Task.CompletedTask;
    }
}
