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
/// What one sweep does with the batch it claimed when it cannot finish it.
/// </summary>
/// <remarks>
/// The claim stamps every run in the batch, and a stamped run is left alone for the whole
/// redispatch window. That is right for a run whose job was started and wrong for every other
/// one, so the sweep's obligation is to take the stamp off whatever it did not start — and to
/// do so however the batch ended, because the trigger is an HTTP client and the refusal record
/// is a database write, and either can throw. No database here: the claim is Postgres's and is
/// tested in <c>DispatchClaimTests</c>; what is tested is what the sweep hands back to it.
/// </remarks>
public class RunDispatcherTests
{
    [Fact]
    public async Task A_trigger_that_throws_mid_batch_still_releases_the_rest_of_it()
    {
        Guid[] runs = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];
        var queue = new SpyQueue(runs);
        var trigger = new StubTrigger(JobStartOutcome.Started, throwOn: runs[1]);

        var released = await SweepOnceAsync(queue, trigger);

        // The first was started and keeps its stamp. The second threw, so nothing was started
        // for it; the third was never asked about. Both go back to the queue now rather than
        // after the redispatch window.
        released.ShouldBe([runs[1], runs[2]]);
        trigger.Asked.ShouldBe([runs[0], runs[1]]);
    }

    [Fact]
    public async Task A_deferral_releases_the_deferred_run_and_everything_behind_it()
    {
        Guid[] runs = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];
        var queue = new SpyQueue(runs);
        var trigger = new StubTrigger(JobStartOutcome.Deferred);

        var released = await SweepOnceAsync(queue, trigger);

        // The ceiling, or an unreachable platform, applies to the whole batch: the sweep stops
        // at the first deferral and asks nothing more.
        released.ShouldBe(runs);
        trigger.Asked.ShouldBe([runs[0]]);
    }

    [Fact]
    public async Task A_refusal_is_recorded_rather_than_released()
    {
        Guid[] runs = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        var queue = new SpyQueue(runs);
        var trigger = new StubTrigger(JobStartOutcome.Rejected);

        var released = await SweepOnceAsync(queue, trigger);

        // The refusal record clears the stamp itself and counts the attempt; releasing on top
        // of it would be harmless but would hide that the count is what fails the run.
        queue.Refused.ShouldBe(runs);
        released.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_release_that_fails_after_a_throw_does_not_hide_what_threw()
    {
        Guid[] runs = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        var queue = new SpyQueue(runs, releaseFailure: new InvalidOperationException("The release fell over."));
        var trigger = new StubTrigger(JobStartOutcome.Started, throwOn: runs[0]);
        var log = new SpyLogger();

        await SweepOnceAsync(queue, trigger, log);

        // The trigger's exception is what stopped the batch, so it is the one the sweep
        // reports. A release that throws from the finally would replace it — the likely case
        // being a refusal record and a release failing the same way on the same context — so
        // the release's own failure is logged beside it, not in its place.
        log.Errors.ShouldBe(
        [
            ("Could not release 2 claimed runs after the sweep failed", "The release fell over."),
            ("Run dispatch sweep failed", "The trigger fell over."),
        ]);
    }

    /// <summary>
    /// Runs the dispatcher through its own hosted-service entry point until the sweep has
    /// handed its release back to the queue, then stops it. The sweep interval is the longest
    /// allowed, so exactly one sweep happens.
    /// </summary>
    private static async Task<IReadOnlyCollection<Guid>> SweepOnceAsync(
        SpyQueue queue, IJobTrigger trigger, ILogger<RunDispatcher>? logger = null)
    {
        var ct = TestContext.Current.CancellationToken;

        var services = new ServiceCollection()
            .AddMetrics()
            .AddScoped<IPipelineRunQueue>(_ => queue)
            .AddSingleton<PipelineMetrics>()
            .BuildServiceProvider();

        var options = Options.Create(new PipelineJobOptions
        {
            BaseUrl = "https://jobs.test",
            Region = "fr-par",
            JobDefinitionId = "spacecraft-pipeline",
            SecretKey = "key",
            SweepSeconds = 300,
        });

        using var dispatcher = new RunDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            trigger,
            services.GetRequiredService<PipelineMetrics>(),
            options,
            logger ?? NullLogger<RunDispatcher>.Instance);

        await dispatcher.StartAsync(ct);
        var released = await queue.Released.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        // StopAsync waits for the sweep to end, so everything it logged is in by now.
        await dispatcher.StopAsync(ct);

        return released;
    }

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

    private sealed class StubTrigger(JobStartOutcome outcome, Guid? throwOn = null) : IJobTrigger
    {
        public List<Guid> Asked { get; } = [];

        public Task<JobStartResult> StartAsync(Guid runId, CancellationToken ct = default)
        {
            Asked.Add(runId);

            if (runId == throwOn)
            {
                throw new InvalidOperationException("The trigger fell over.");
            }

            return Task.FromResult(new JobStartResult(
                outcome, outcome is JobStartOutcome.Started ? "job" : null, "detail"));
        }
    }

    /// <summary>
    /// Hands out one batch, then nothing, and records what the sweep gives back.
    /// </summary>
    private sealed class SpyQueue(IReadOnlyList<Guid> batch, Exception? releaseFailure = null) : IPipelineRunQueue
    {
        private bool _claimed;

        public TaskCompletionSource<IReadOnlyCollection<Guid>> Released { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<Guid> Refused { get; } = [];

        public Task<IReadOnlyList<Guid>> ClaimForDispatchAsync(
            int batchSize, TimeSpan redispatchAfter, CancellationToken ct = default)
        {
            if (_claimed)
            {
                return Task.FromResult<IReadOnlyList<Guid>>([]);
            }

            _claimed = true;
            return Task.FromResult(batch);
        }

        public Task ReleaseDispatchAsync(IReadOnlyCollection<Guid> runIds, CancellationToken ct = default)
        {
            Released.TrySetResult(runIds);
            return releaseFailure is null ? Task.CompletedTask : Task.FromException(releaseFailure);
        }

        public Task<bool> RecordDispatchRefusalAsync(Guid runId, string reason, CancellationToken ct = default)
        {
            Refused.Add(runId);
            return Task.FromResult(true);
        }

        public Task<PipelineRun?> TryClaimAsync(Guid runId, string workerId, CancellationToken ct = default) =>
            throw new NotSupportedException("The dispatcher starts jobs; it does not execute runs.");

        public Task<PipelineRun?> TryClaimAsync(string workerId, CancellationToken ct = default) =>
            throw new NotSupportedException("The dispatcher starts jobs; it does not execute runs.");

        public Task<(int Count, TimeSpan OldestAge)> QueueDepthAsync(CancellationToken ct = default) =>
            throw new NotSupportedException("Depth is the metrics' question, not the sweep's.");
    }
}
