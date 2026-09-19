using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpaceCraft.Api.Features.Runs;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// What one sweep does with the batch it claimed.
/// </summary>
/// <remarks>
/// The claim is Postgres's and is tested against a real one in <c>DispatchClaimTests</c>; the
/// trigger's reading of the platform's answer is <c>JobTriggerTests</c>. Between the two sits
/// the composition — which runs are asked for, which are released so the next sweep retries
/// them at once, which are counted as refused — the piece where a wrong branch spends an
/// execution against the parallel-run ceiling or leaves a run stamped for a job that never
/// existed. The sweep's obligation is to take the stamp off whatever it did not start — and
/// to do so however the batch ended, because the trigger is an HTTP client and the refusal
/// record is a database write, and either can throw.
/// <para>
/// No database. The queue is a fake that hands back a scripted batch and records what comes
/// back to it, the trigger answers each run as scripted, and the metrics are the real ones
/// over an in-process meter, read back through a listener. The cases call
/// <see cref="RunDispatcher.SweepAsync"/> directly rather than starting the service and
/// waiting for its timer, which is what they used to do: a sweep called is a sweep that ran,
/// with nothing to wait for and nothing to time out. The one case that needs the timer is the
/// one about the loop itself — a sweep that throws must not end the service — and that one
/// goes through <c>StartAsync</c>.
/// </para>
/// </remarks>
public class RunDispatcherTests
{
    private static readonly Guid First = Guid.CreateVersion7();
    private static readonly Guid Second = Guid.CreateVersion7();
    private static readonly Guid Third = Guid.CreateVersion7();

    [Fact]
    public async Task A_batch_that_all_starts_leaves_nothing_to_release()
    {
        var queue = new RecordingQueue(First, Second);
        var trigger = new ScriptedTrigger
        {
            [First] = Started("job-1"),
            [Second] = Started("job-2"),
        };

        var counted = await SweepAsync(queue, trigger);

        trigger.Asked.ShouldBe([First, Second]);

        // Every run has a job; the stamps stay on so the next sweep leaves them alone until
        // the redispatch window passes or a worker claims them.
        queue.Released.ShouldBeEmpty();
        queue.Refusals.ShouldBeEmpty();
        counted.ShouldBe(new JobCounts(Started: 2, Deferred: 0, Refused: 0));
    }

    [Fact]
    public async Task A_deferral_stops_the_batch_and_releases_what_was_not_asked_for()
    {
        var queue = new RecordingQueue(First, Second, Third);
        var trigger = new ScriptedTrigger
        {
            [First] = Started("job-1"),
            [Second] = new JobStartResult(JobStartOutcome.Deferred, null, "quota exceeded"),
        };

        var counted = await SweepAsync(queue, trigger);

        // The ceiling applies to the rest of the batch too, so the third is never asked for.
        trigger.Asked.ShouldBe([First, Second]);

        // The deferred run and every run after it come off the stamp, or they would wait out
        // the whole window for a job that was never started. The one that started keeps it.
        queue.Released.ShouldBe([Second, Third]);
        queue.Refusals.ShouldBeEmpty();

        // One deferral counted, not two: the third was released without being asked.
        counted.ShouldBe(new JobCounts(Started: 1, Deferred: 1, Refused: 0));
    }

    [Fact]
    public async Task A_rejection_is_recorded_against_the_run_and_the_batch_goes_on()
    {
        var queue = new RecordingQueue(First, Second);
        var trigger = new ScriptedTrigger
        {
            [First] = new JobStartResult(JobStartOutcome.Rejected, null, "404 Not Found. job definition not found"),
            [Second] = Started("job-2"),
        };

        var counted = await SweepAsync(queue, trigger);

        // A deployment fault on one run says nothing about the next, so the sweep keeps going.
        trigger.Asked.ShouldBe([First, Second]);

        // Counted rather than released: the refusal itself clears the stamp, and it is what
        // fails the run once it has happened enough times.
        var refusal = queue.Refusals.ShouldHaveSingleItem();
        refusal.RunId.ShouldBe(First);
        refusal.Reason.ShouldBe("404 Not Found. job definition not found");
        queue.Released.ShouldBeEmpty();
        counted.ShouldBe(new JobCounts(Started: 1, Deferred: 0, Refused: 1));
    }

    [Fact]
    public async Task An_empty_batch_asks_the_platform_for_nothing()
    {
        var queue = new RecordingQueue();
        var trigger = new ScriptedTrigger();

        var counted = await SweepAsync(queue, trigger);

        trigger.Asked.ShouldBeEmpty();
        queue.Released.ShouldBeEmpty();
        counted.ShouldBe(new JobCounts(Started: 0, Deferred: 0, Refused: 0));
    }

    [Fact]
    public async Task A_trigger_that_throws_mid_batch_still_releases_the_rest_of_it()
    {
        var queue = new RecordingQueue(First, Second, Third);
        var trigger = new ScriptedTrigger
        {
            [First] = Started("job-1"),
        };
        trigger.ThrowOn(Second);

        await Should.ThrowAsync<InvalidOperationException>(() => SweepAsync(queue, trigger));

        // The first was started and keeps its stamp. The second threw, so nothing was started
        // for it; the third was never asked about. Both go back to the queue now rather than
        // after the redispatch window.
        trigger.Asked.ShouldBe([First, Second]);
        queue.Released.ShouldBe([Second, Third]);
    }

    [Fact]
    public async Task A_release_that_fails_after_a_throw_does_not_hide_what_threw()
    {
        var queue = new RecordingQueue(First, Second);
        queue.FailRelease(new InvalidOperationException("The release fell over."));
        var trigger = new ScriptedTrigger();
        trigger.ThrowOn(First);
        var log = new SpyLogger();

        await using var services = Services(queue, _ => trigger);
        using var metrics = MetricsOver(services);
        using var dispatcher = DispatcherFor(services, metrics, sweepSeconds: 1, log);

        // The trigger's exception is what stopped the batch, so it is the one the sweep
        // raises. A release that throws from the finally would replace it — the likely case
        // being a refusal record and a release failing the same way on the same context — so
        // the release's own failure is logged beside it, not raised in its place.
        var raised = await Should.ThrowAsync<InvalidOperationException>(
            () => dispatcher.SweepAsync(TestContext.Current.CancellationToken));

        raised.Message.ShouldContain("something unreadable");
        log.Errors.ShouldBe([("Could not release 2 claimed runs after the sweep failed", "The release fell over.")]);
    }

    [Fact]
    public async Task The_trigger_is_resolved_for_each_sweep_not_once_for_the_service()
    {
        // The trigger is a typed HttpClient, transient so the factory can rotate its handler.
        // Captured once by this singleton it would keep the first handler — and its DNS
        // answer — for the life of the process. So each sweep asks its own scope for one.
        var queue = new RecordingQueue();
        var resolved = 0;

        await using var services = Services(queue, _ =>
        {
            resolved++;
            return new ScriptedTrigger();
        });
        using var metrics = MetricsOver(services);
        using var dispatcher = DispatcherFor(services, metrics, sweepSeconds: 1);

        await dispatcher.SweepAsync(TestContext.Current.CancellationToken);
        await dispatcher.SweepAsync(TestContext.Current.CancellationToken);

        resolved.ShouldBe(2);
    }

    [Fact]
    public async Task A_sweep_that_throws_does_not_take_the_service_with_it()
    {
        // Through the hosted-service entry point, because what is under test is the loop
        // around the sweep: a sweep that throws — the database gone for a moment — is logged
        // and the next tick sweeps again. A dispatcher that stopped instead would be the
        // absence it exists to fix, and nothing would say so.
        var ct = TestContext.Current.CancellationToken;
        var queue = new RecordingQueue();
        queue.FailNextClaim(new InvalidOperationException("The database went away for one sweep."));

        await using var services = Services(queue, _ => new ScriptedTrigger());
        using var metrics = MetricsOver(services);
        using var dispatcher = DispatcherFor(services, metrics, sweepSeconds: 1);

        await dispatcher.StartAsync(ct);
        await queue.ClaimedAgain.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        await dispatcher.StopAsync(ct);

        // The first claim threw; the second could only have come from the loop going on.
        queue.Claims.ShouldBeGreaterThanOrEqualTo(2);
    }

    private static JobStartResult Started(string jobRunId) =>
        new(JobStartOutcome.Started, jobRunId, null);

    /// <summary>
    /// One sweep, called directly, over the dispatcher as the host builds it: a scope factory
    /// that resolves the fake queue and the trigger, the real metrics over an in-process
    /// meter, the options' defaults. Returns what the sweep counted.
    /// </summary>
    private static async Task<JobCounts> SweepAsync(RecordingQueue queue, ScriptedTrigger trigger)
    {
        await using var services = Services(queue, _ => trigger);
        using var metrics = MetricsOver(services);
        using var counters = new JobCounters(services.GetRequiredService<IMeterFactory>());
        using var dispatcher = DispatcherFor(services, metrics, sweepSeconds: 1);

        await dispatcher.SweepAsync(TestContext.Current.CancellationToken);

        return counters.Snapshot();
    }

    private static ServiceProvider Services(RecordingQueue queue, Func<IServiceProvider, IJobTrigger> trigger) =>
        // Both resolved from the sweep's scope, as the dispatcher does: the trigger is a
        // typed HttpClient and must not be captured for the life of the service.
        new ServiceCollection()
            .AddMetrics()
            .AddScoped<IPipelineRunQueue>(_ => queue)
            .AddScoped(trigger)
            .BuildServiceProvider();

    /// <summary>Owned by the test rather than the provider, so that the test disposes it.</summary>
    private static PipelineMetrics MetricsOver(ServiceProvider services) =>
        new(
            services.GetRequiredService<IMeterFactory>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<PipelineMetrics>.Instance);

    private static RunDispatcher DispatcherFor(
        ServiceProvider services, PipelineMetrics metrics, int sweepSeconds, ILogger<RunDispatcher>? logger = null) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            metrics,
            Options.Create(new PipelineJobOptions { SweepSeconds = sweepSeconds }),
            logger ?? NullLogger<RunDispatcher>.Instance);

    /// <summary>Keeps the errors, since the question is which exception reached the log.</summary>
    private sealed class SpyLogger : ILogger<RunDispatcher>
    {
        /// <summary>Message and exception message of every error, in the order they were logged.</summary>
        public List<(string Message, string? Exception)> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                Errors.Add((formatter(state, exception), exception?.Message));
            }
        }
    }

    private sealed record JobCounts(long Started, long Deferred, long Refused);

    /// <summary>
    /// Totals the three job counters as the sweep increments them. Listens to the instruments
    /// of one meter factory only — the factory is the meter's scope — so another test's
    /// meter of the same name, running alongside, is not counted here.
    /// </summary>
    private sealed class JobCounters : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly Dictionary<string, long> _totals = new(StringComparer.Ordinal);

        public JobCounters(IMeterFactory factory)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter.Scope, factory))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
                _totals[instrument.Name] = _totals.GetValueOrDefault(instrument.Name) + value);

            _listener.Start();
        }

        public JobCounts Snapshot() => new(
            _totals.GetValueOrDefault("spacecraft.jobs.started"),
            _totals.GetValueOrDefault("spacecraft.jobs.deferred"),
            _totals.GetValueOrDefault("spacecraft.jobs.refused"));

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>
    /// Answers each run with what the test scripted for it, and fails the test on a run it
    /// was never told about — a sweep asking for a run outside its batch is a bug, not a
    /// case to answer.
    /// </summary>
    private sealed class ScriptedTrigger : IJobTrigger
    {
        private readonly Dictionary<Guid, JobStartResult> _answers = [];

        public List<Guid> Asked { get; } = [];

        private readonly HashSet<Guid> _throwing = [];

        public JobStartResult this[Guid runId]
        {
            set => _answers[runId] = value;
        }

        /// <summary>Scripts the one kind of answer a trigger gives without meaning to.</summary>
        public void ThrowOn(Guid runId) => _throwing.Add(runId);

        public Task<JobStartResult> StartAsync(Guid runId, CancellationToken ct = default)
        {
            Asked.Add(runId);

            if (_throwing.Contains(runId))
            {
                throw new InvalidOperationException($"The platform answered run {runId} with something unreadable.");
            }

            return _answers.TryGetValue(runId, out var answer)
                ? Task.FromResult(answer)
                : throw new InvalidOperationException($"No answer was scripted for run {runId}.");
        }
    }

    /// <summary>
    /// Hands back the batch it was built with on every claim, and records what the sweep
    /// gives back. The claims a sweep must never make throw, so reaching for one fails the
    /// test.
    /// </summary>
    private sealed class RecordingQueue(params Guid[] batch) : IPipelineRunQueue
    {
        private Exception? _nextClaimFailure;

        public int Claims { get; private set; }

        /// <summary>Completes once a second claim has been made — a second sweep has run.</summary>
        public TaskCompletionSource ClaimedAgain { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<Guid> Released { get; } = [];

        public List<(Guid RunId, string Reason)> Refusals { get; } = [];

        private Exception? _releaseFailure;

        /// <summary>Scripts the claim that throws — the database gone for a moment.</summary>
        public void FailNextClaim(Exception failure) => _nextClaimFailure = failure;

        /// <summary>Scripts a release that throws, as one does when its context is already broken.</summary>
        public void FailRelease(Exception failure) => _releaseFailure = failure;

        public Task<IReadOnlyList<Guid>> ClaimForDispatchAsync(
            int batchSize, TimeSpan redispatchAfter, CancellationToken ct = default)
        {
            Claims++;

            if (Claims > 1)
            {
                ClaimedAgain.TrySetResult();
            }

            if (Interlocked.Exchange(ref _nextClaimFailure, null) is { } failure)
            {
                throw failure;
            }

            return Task.FromResult<IReadOnlyList<Guid>>(batch);
        }

        public Task ReleaseDispatchAsync(IReadOnlyCollection<Guid> runIds, CancellationToken ct = default)
        {
            Released.AddRange(runIds);
            return _releaseFailure is null ? Task.CompletedTask : Task.FromException(_releaseFailure);
        }

        public Task<bool> RecordDispatchRefusalAsync(Guid runId, string reason, CancellationToken ct = default)
        {
            Refusals.Add((runId, reason));
            return Task.FromResult(true);
        }

        public Task<PipelineRun?> TryClaimAsync(string workerId, CancellationToken ct = default) =>
            throw new NotSupportedException("A sweep starts jobs; it never executes a run itself.");

        public Task<PipelineRun?> TryClaimAsync(Guid runId, string workerId, CancellationToken ct = default) =>
            throw new NotSupportedException("A sweep starts jobs; it never executes a run itself.");

        public Task<(int Count, TimeSpan OldestAge)> QueueDepthAsync(CancellationToken ct = default) =>
            throw new NotSupportedException("Depth is read by the metrics gauge, not by a sweep.");
    }
}
