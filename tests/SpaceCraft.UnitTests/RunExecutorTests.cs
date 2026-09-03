using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Pipeline.Execution;
using SpaceCraft.Runner;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// What one invocation of the runner does with the run it was handed.
/// </summary>
/// <remarks>
/// The branch worth having a test for is the one nothing else produces. End-to-end runs cover
/// the happy path, but nothing there ever loses a race: two API instances have to sweep at the
/// same moment for a container to arrive and find its run already claimed. That case is written
/// down as safe in a comment in the dispatch tests — "a second job would find nothing and exit"
/// — and until now it was asserted nowhere.
/// <para>
/// No database here. The claim itself is Postgres's and is tested against a real one in
/// <c>DispatchClaimTests</c>; what is tested here is what the runner makes of the answer.
/// </para>
/// </remarks>
public class RunExecutorTests
{
    [Fact]
    public async Task A_run_it_cannot_claim_is_nothing_to_do_rather_than_a_fault()
    {
        var pipeline = new SpyPipeline();
        var executor = ExecutorFor(new StubQueue(claims: null), pipeline);

        var outcome = await executor.ExecuteAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        // Somebody else is executing it, or it was cancelled between the dispatch and the start.
        // Both are ordinary, and the exit code that follows from this is zero.
        outcome.ShouldBe(RunnerOutcome.NothingToDo);

        // The part that actually matters: it did not go on to execute the run anyway. Two
        // containers building one run would publish the same version twice.
        pipeline.Executed.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_claimed_run_is_the_one_that_gets_executed()
    {
        var run = QueuedRun();
        var pipeline = new SpyPipeline();
        var executor = ExecutorFor(new StubQueue(claims: run), pipeline);

        var outcome = await executor.ExecuteAsync(run.Id, TestContext.Current.CancellationToken);

        outcome.ShouldBe(RunnerOutcome.Completed);

        // Executed by the id the queue handed back, not the id the caller asked for. They are
        // the same today; taking it from the claim is what keeps them the same tomorrow.
        pipeline.Executed.ShouldBe([run.Id]);
    }

    [Fact]
    public async Task The_claim_is_for_the_run_it_was_asked_for_and_signed_by_this_process()
    {
        var queue = new StubQueue(claims: null);
        var wanted = Guid.CreateVersion7();

        await ExecutorFor(queue, new SpyPipeline())
            .ExecuteAsync(wanted, TestContext.Current.CancellationToken);

        queue.AskedFor.ShouldBe(wanted);

        // The claimant is written to the runs table and is the only trace left when a container
        // dies mid-run: the lease lapses, the sweep requeues it, and this string is what says
        // which invocation was holding it.
        queue.ClaimedBy.ShouldNotBeNull();
        queue.ClaimedBy.ShouldContain(Environment.MachineName);
        queue.ClaimedBy.ShouldContain(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static RunExecutor ExecutorFor(IPipelineRunQueue queue, IPipelineExecutor pipeline)
    {
        var services = new ServiceCollection()
            .AddScoped(_ => queue)
            .AddScoped(_ => pipeline)
            .BuildServiceProvider();

        return new RunExecutor(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RunExecutor>.Instance);
    }

    /// <summary>A queued run, built in memory — the domain object needs no database to exist.</summary>
    private static PipelineRun QueuedRun()
    {
        var organization = new Organization("Acme", "acme", Guid.CreateVersion7());
        var project = organization.AddProject("Billing", "billing");
        var target = project.AddTarget(SdkLanguage.TypeScript, "billing-sdk", RegistryKind.Npm);
        var spec = project.AddSpecVersion(id => $"specs/{id}.yaml", "hash", 10, SpecSource.Upload);

        return new PipelineRun(
            organization.Id, project.Id, spec.Id, spec.Sequence, target,
            "https://registry.npmjs.org", "npm", RunTrigger.Manual, publishRequested: false);
    }

    /// <summary>
    /// Answers the one claim the runner makes and refuses the rest. The refusals are the point:
    /// this process is triggered for a named run, and reaching for the queue-draining claim or
    /// the dispatch sweep from inside a job would be a mistake worth failing a test over.
    /// </summary>
    private sealed class StubQueue(PipelineRun? claims) : IPipelineRunQueue
    {
        public Guid? AskedFor { get; private set; }

        public string? ClaimedBy { get; private set; }

        public Task<PipelineRun?> TryClaimAsync(Guid runId, string workerId, CancellationToken ct = default)
        {
            AskedFor = runId;
            ClaimedBy = workerId;
            return Task.FromResult(claims);
        }

        public Task<PipelineRun?> TryClaimAsync(string workerId, CancellationToken ct = default) =>
            throw new NotSupportedException("A runner is invoked for one named run; it does not drain the queue.");

        public Task<IReadOnlyList<Guid>> ClaimForDispatchAsync(
            int batchSize, TimeSpan redispatchAfter, CancellationToken ct = default) =>
            throw new NotSupportedException("Dispatch belongs to the API, not to the job it starts.");

        public Task ReleaseDispatchAsync(IReadOnlyCollection<Guid> runIds, CancellationToken ct = default) =>
            throw new NotSupportedException("Dispatch belongs to the API, not to the job it starts.");

        public Task<bool> RecordDispatchRefusalAsync(Guid runId, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException("Dispatch belongs to the API, not to the job it starts.");

        public Task<(int Count, TimeSpan OldestAge)> QueueDepthAsync(CancellationToken ct = default) =>
            throw new NotSupportedException("A job has no business measuring the queue it came from.");
    }

    private sealed class SpyPipeline : IPipelineExecutor
    {
        public List<Guid> Executed { get; } = [];

        public Task ExecuteAsync(Guid runId, CancellationToken ct = default)
        {
            Executed.Add(runId);
            return Task.CompletedTask;
        }
    }
}
