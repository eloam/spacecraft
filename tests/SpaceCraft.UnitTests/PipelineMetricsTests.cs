using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceCraft.Api.Features.Runs;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The two queue gauges are one reading, taken once per collection, kept when the next one
/// fails, and absent rather than zero while none has succeeded.
/// </summary>
/// <remarks>
/// Driven through a MeterListener, the way dotnet-counters or an exporter would read them,
/// because what is being pinned is what a collector sees: one query behind both numbers, the
/// same instant behind both, and a number rather than an aborted collection when the database
/// is away.
/// </remarks>
public sealed class PipelineMetricsTests
{
    [Fact]
    public void Both_gauges_come_from_one_reading_of_the_queue()
    {
        var queue = new CountingQueue { Depth = (3, TimeSpan.FromSeconds(42)) };
        using var metrics = Metrics(queue, new FrozenClock());
        using var listener = Listen(out var readings);

        listener.RecordObservableInstruments();

        queue.Reads.ShouldBe(1);
        readings["spacecraft.runs.queued"].ShouldBe(3);
        readings["spacecraft.runs.queued.oldest_age"].ShouldBe(42);
    }

    [Fact]
    public void The_next_collection_reads_the_queue_again()
    {
        var queue = new CountingQueue { Depth = (3, TimeSpan.FromSeconds(42)) };
        var clock = new FrozenClock();
        using var metrics = Metrics(queue, clock);
        using var listener = Listen(out var readings);

        listener.RecordObservableInstruments();

        // Well past the lifetime of one reading, as collections are.
        clock.Advance(TimeSpan.FromSeconds(5));
        queue.Depth = (0, TimeSpan.Zero);

        listener.RecordObservableInstruments();

        queue.Reads.ShouldBe(2);
        readings["spacecraft.runs.queued"].ShouldBe(0);
        readings["spacecraft.runs.queued.oldest_age"].ShouldBe(0);
    }

    [Fact]
    public void A_reading_that_fails_serves_the_last_one_rather_than_abort_the_collection()
    {
        var queue = new CountingQueue { Depth = (3, TimeSpan.FromSeconds(42)) };
        var clock = new FrozenClock();
        using var metrics = Metrics(queue, clock);
        using var listener = Listen(out var readings);

        listener.RecordObservableInstruments();

        clock.Advance(TimeSpan.FromSeconds(5));
        queue.Failure = new InvalidOperationException("The database is away.");

        // No exception: one thrown from a gauge's callback ends the whole collection, counters
        // included, and the numbers from the last good reading are better than none.
        Should.NotThrow(listener.RecordObservableInstruments);

        queue.Reads.ShouldBe(2);
        readings["spacecraft.runs.queued"].ShouldBe(3);
        readings["spacecraft.runs.queued.oldest_age"].ShouldBe(42);

        // And it is a failure, not a lifetime: the next collection asks again.
        clock.Advance(TimeSpan.FromSeconds(5));
        queue.Failure = null;
        queue.Depth = (1, TimeSpan.FromSeconds(7));

        listener.RecordObservableInstruments();

        readings["spacecraft.runs.queued"].ShouldBe(1);
        readings["spacecraft.runs.queued.oldest_age"].ShouldBe(7);
    }

    [Fact]
    public void A_first_reading_that_fails_publishes_nothing_rather_than_an_empty_queue()
    {
        var queue = new CountingQueue { Failure = new InvalidOperationException("The database is away.") };
        var clock = new FrozenClock();
        using var metrics = Metrics(queue, clock);
        using var listener = Listen(out var readings);

        // No exception, and no data point either: a zero here would read as an empty queue on
        // an instance that has never managed to look. One attempt for the pair, as for a
        // reading that succeeds.
        Should.NotThrow(listener.RecordObservableInstruments);

        queue.Reads.ShouldBe(1);
        readings.ShouldBeEmpty();

        // And it is a failure, not a lifetime: the next collection asks again and publishes
        // what it reads.
        clock.Advance(TimeSpan.FromSeconds(5));
        queue.Failure = null;
        queue.Depth = (2, TimeSpan.FromSeconds(9));

        listener.RecordObservableInstruments();

        queue.Reads.ShouldBe(2);
        readings["spacecraft.runs.queued"].ShouldBe(2);
        readings["spacecraft.runs.queued.oldest_age"].ShouldBe(9);
    }

    private static PipelineMetrics Metrics(IPipelineRunQueue queue, TimeProvider time)
    {
        var services = new ServiceCollection();
        services.AddSingleton(queue);
        var provider = services.BuildServiceProvider();

        return new PipelineMetrics(
            new MeterFactory(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            time,
            NullLogger<PipelineMetrics>.Instance);
    }

    /// <summary>A collector: every gauge on the meter, its latest value by name.</summary>
    private static MeterListener Listen(out Dictionary<string, double> readings)
    {
        var latest = new Dictionary<string, double>(StringComparer.Ordinal);
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == PipelineMetrics.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => latest[instrument.Name] = value);
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => latest[instrument.Name] = value);
        listener.Start();

        readings = latest;
        return listener;
    }

    private sealed class MeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);

        public void Dispose()
        {
        }
    }

    /// <summary>A clock that moves only when told to.</summary>
    private sealed class FrozenClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>Answers a depth, counts how often it is asked, and fails on request.</summary>
    private sealed class CountingQueue : IPipelineRunQueue
    {
        public (int Count, TimeSpan OldestAge) Depth { get; set; }

        public Exception? Failure { get; set; }

        public int Reads { get; private set; }

        public Task<(int Count, TimeSpan OldestAge)> QueueDepthAsync(CancellationToken ct = default)
        {
            Reads++;
            return Failure is null ? Task.FromResult(Depth) : Task.FromException<(int, TimeSpan)>(Failure);
        }

        public Task<PipelineRun?> TryClaimAsync(Guid runId, string workerId, CancellationToken ct = default) =>
            throw new NotSupportedException("Only the depth is read here.");

        public Task<PipelineRun?> TryClaimAsync(string workerId, CancellationToken ct = default) =>
            throw new NotSupportedException("Only the depth is read here.");

        public Task<IReadOnlyList<Guid>> ClaimForDispatchAsync(
            int batchSize, TimeSpan redispatchAfter, CancellationToken ct = default) =>
            throw new NotSupportedException("Only the depth is read here.");

        public Task ReleaseDispatchAsync(IReadOnlyCollection<Guid> runIds, CancellationToken ct = default) =>
            throw new NotSupportedException("Only the depth is read here.");

        public Task<bool> RecordDispatchRefusalAsync(Guid runId, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException("Only the depth is read here.");
    }
}
