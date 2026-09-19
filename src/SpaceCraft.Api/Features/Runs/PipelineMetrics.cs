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

    private readonly Meter _meter;

    public PipelineMetrics(IMeterFactory factory, IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _meter = factory.Create(MeterName);

        JobsStarted = _meter.CreateCounter<long>(
            "spacecraft.jobs.started", "{job}", "Job executions started for a queued run.");

        JobsDeferred = _meter.CreateCounter<long>(
            "spacecraft.jobs.deferred", "{job}",
            "Starts the platform refused for now — the parallel-run quota, or being unreachable.");

        JobsRefused = _meter.CreateCounter<long>(
            "spacecraft.jobs.refused", "{job}",
            "Starts refused in a way retrying will not fix — an unknown definition, a rejected key.");

        // Observable, so it costs a query only when something is collecting. Both readings come
        // from one scope: a count and an age that disagreed about the moment they were taken
        // would be worse than either alone.
        _meter.CreateObservableGauge(
            "spacecraft.runs.queued",
            () =>
            {
                var (count, age) = Depth(scopeFactory);
                return new Measurement<long>(count);
            },
            "{run}",
            "Runs waiting for a job to be started for them.");

        _meter.CreateObservableGauge(
            "spacecraft.runs.queued.oldest_age",
            () =>
            {
                var (_, age) = Depth(scopeFactory);
                return new Measurement<double>(age.TotalSeconds);
            },
            "s",
            "How long the oldest queued run has been waiting. The alert worth having.");
    }

    public Counter<long> JobsStarted { get; }

    public Counter<long> JobsDeferred { get; }

    public Counter<long> JobsRefused { get; }

    private static (int Count, TimeSpan OldestAge) Depth(IServiceScopeFactory scopeFactory)
    {
        // Synchronous by necessity: the observable-instrument callback is not async, and a
        // collection that hangs would be worse than one that blocks briefly on a counting
        // query against a partial index.
        using var scope = scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>();
        return queue.QueueDepthAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _meter.Dispose();
}
