using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using SpaceCraft.Infrastructure.Persistence;

namespace SpaceCraft.Api.Features.Runs;

/// <summary>
/// What the pipeline queue is doing, as numbers rather than log lines.
/// </summary>
/// <remarks>
/// There were none before this, and the absence had a cost: nothing read the run queue in
/// production for months, runs piled up at <c>Queued</c>, and the only way to notice was to
/// open psql and count rows. <c>queued.oldest_age</c> is the one number that would have said
/// so on its own — it grows without bound the moment starting a job stops working, for any
/// reason, including reasons nobody has thought of yet.
/// <para>
/// Instruments only. No exporter is registered here: OpenTelemetry ingestion is billed from
/// the first sample, and this stack's whole compute bill is smaller than a careless collector
/// would be. These are readable with <c>dotnet-counters</c> today and can be pointed at a
/// collector the day somebody decides to pay for one, which is the cheap half of the work and
/// the half that has to exist first.
/// </para>
/// </remarks>
public sealed class PipelineMetrics : IDisposable
{
    public const string MeterName = "SpaceCraft.Pipeline";

    /// <summary>
    /// How long one reading of the queue serves both gauges. A collection reads the two
    /// instruments within milliseconds of each other, so a second is comfortably "this
    /// collection". The fastest collector this is read with — <c>dotnet-counters</c> —
    /// refreshes once a second, so at worst a collection that lands just inside the window
    /// is served the reading before it: a second old, on a display that moves once a second.
    /// </summary>
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(1);

    private readonly Meter _meter;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _time;
    private readonly ILogger<PipelineMetrics> _logger;

    private readonly Lock _gate = new();

    /// <summary>The last reading taken, or null while none has succeeded.</summary>
    private QueueSnapshot? _snapshot;

    /// <summary>When the queue was last asked, whether or not it answered.</summary>
    private DateTimeOffset _attemptedAt = DateTimeOffset.MinValue;

    public PipelineMetrics(
        IMeterFactory factory,
        IServiceScopeFactory scopeFactory,
        TimeProvider time,
        ILogger<PipelineMetrics> logger)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _time = time;
        _logger = logger;
        _meter = factory.Create(MeterName);

        JobsStarted = _meter.CreateCounter<long>(
            "spacecraft.jobs.started", "{job}", "Job executions started for a queued run.");

        JobsDeferred = _meter.CreateCounter<long>(
            "spacecraft.jobs.deferred", "{job}",
            "Starts the platform refused for now — the parallel-run quota, or being unreachable.");

        JobsRefused = _meter.CreateCounter<long>(
            "spacecraft.jobs.refused", "{job}",
            "Starts refused in a way retrying will not fix — an unknown definition, a rejected key.");

        // Observable, so it costs a query only when something is collecting — and one query
        // for the pair, since a count and an age that disagreed about the moment they were
        // taken would be worse than either alone. The overload that observes a sequence, so
        // that no reading at all is no data point rather than a zero: an empty queue and a
        // database that has never answered must not look alike.
        _meter.CreateObservableGauge<long>(
            "spacecraft.runs.queued",
            () => Snapshot() is { } queue ? [new Measurement<long>(queue.Count)] : [],
            "{run}",
            "Runs waiting for a job to be started for them.");

        _meter.CreateObservableGauge<double>(
            "spacecraft.runs.queued.oldest_age",
            () => Snapshot() is { } queue ? [new Measurement<double>(queue.OldestAge.TotalSeconds)] : [],
            "s",
            "How long the oldest queued run has been waiting. The alert worth having.");
    }

    public Counter<long> JobsStarted { get; }

    public Counter<long> JobsDeferred { get; }

    public Counter<long> JobsRefused { get; }

    /// <summary>
    /// The queue as it was at the start of this collection: read once, served to both gauges.
    /// Null when no reading has ever succeeded, and the gauges then publish nothing.
    /// </summary>
    /// <remarks>
    /// Synchronous by necessity: an observable instrument's callback is not async, and a
    /// collection that hangs would be worse than one that blocks briefly on one aggregate over
    /// a partial index. Under the lock, so two collectors reading at once — dotnet-counters
    /// beside an exporter — share a reading rather than each taking one.
    /// <para>
    /// A reading that fails serves the last one taken, with a warning, rather than throw: an
    /// exception in the callback aborts the whole collection, counters included, and the moment
    /// the database is unreachable is not the moment to lose every other number. A failure
    /// before any reading has succeeded has nothing to serve and serves nothing — not zeros,
    /// which would read as an empty queue. The attempt is stamped whether or not it answered,
    /// so a collection costs one query while the database is away, and the next collection
    /// tries again.
    /// </para>
    /// </remarks>
    private QueueSnapshot? Snapshot()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();

            if (now - _attemptedAt < SnapshotLifetime)
            {
                return _snapshot;
            }

            _attemptedAt = now;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>();
                var (count, age) = queue.QueueDepthAsync().GetAwaiter().GetResult();
                _snapshot = new QueueSnapshot(count, age);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reading the run queue for its gauges failed; the last reading is served");
            }

            return _snapshot;
        }
    }

    public void Dispose() => _meter.Dispose();

    private sealed record QueueSnapshot(int Count, TimeSpan OldestAge);
}
