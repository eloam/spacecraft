using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpaceCraft.Domain.Notifications;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Notifications;
using SpaceCraft.Infrastructure.OpenApi;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Security;
using SpaceCraft.Infrastructure.Storage;
using SpaceCraft.Pipeline;
using SpaceCraft.Pipeline.Execution;
using SpaceCraft.Pipeline.Generation;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// A run is written by two processes that never see each other's copy: the API, which
/// cancels it or sweeps it back once its lease lapses, and the runner, which drives it. The
/// database is the only place the two can disagree, so this is where the disagreement has to
/// be settled.
/// </summary>
/// <remarks>
/// Needs a real Postgres because the token is Postgres's own: xmin, the transaction id that
/// last wrote the row. An in-memory provider has no such column and would prove nothing.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class RunConcurrencyTests(SpaceCraftApi api)
{
    private const string Spec = """
        openapi: 3.0.3
        info:
          title: Billing API
          version: 1.0.0
        paths: {}
        """;

    // What the sweep writes on a run whose lease lapsed, and on the step it was on.
    private const string LeaseExpired = "Worker lease expired; the worker likely crashed or was evicted.";

    [Fact]
    public async Task A_success_written_over_a_cancel_is_refused_by_the_database()
    {
        var runId = await SeedRunningRunAsync();

        // The runner's copy, loaded before anything else happens to the row.
        await using var runnerScope = api.Services.CreateAsyncScope();
        var runnerDb = runnerScope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var runnersCopy = await runnerDb.PipelineRuns
            .Include(r => r.Steps)
            .SingleAsync(r => r.Id == runId, TestContext.Current.CancellationToken);

        // The API, in another process, cancels it meanwhile.
        await using (var apiScope = api.Services.CreateAsyncScope())
        {
            var apiDb = apiScope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
            var apisCopy = await apiDb.PipelineRuns
                .Include(r => r.Steps)
                .SingleAsync(r => r.Id == runId, TestContext.Current.CancellationToken);

            apisCopy.Cancel("Cancelled by the owner.");
            await apiDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // The runner reaches the end and writes what it knows. Matching on id alone, this
        // used to land, and the run read Succeeded with the cancellation gone.
        runnersCopy.Succeed();

        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => runnerDb.SaveChangesAsync(TestContext.Current.CancellationToken));

        var stored = await ReadAsync(runId);
        stored.Status.ShouldBe(RunStatus.Cancelled);
        stored.FailureReason.ShouldBe("Cancelled by the owner.");
    }

    [Fact]
    public async Task A_run_cancelled_during_a_step_ends_cancelled_and_tells_nobody()
    {
        var runId = await SeedRunningRunAsync();

        await using var worker = api.Services.CreateAsyncScope();
        var workersDb = worker.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var told = new RecordingPublisher(workersDb);

        // The step itself cancels the run through another context — what a cancel from the
        // API looks like from the executor's side, timed to land while the step runs.
        await ExecuteAsync(
            worker.ServiceProvider, runId,
            new InterferingValidator(api, runId, run => run.Cancel("Cancelled mid-step.")), told);

        var stored = await ReadAsync(runId);

        // The executor's own save was refused, it reloaded, and it stopped: the version it
        // resolved never landed, and the cancel is what the row says.
        stored.Status.ShouldBe(RunStatus.Cancelled);
        stored.ResolvedVersion.ShouldBeNull();
        stored.FailureReason.ShouldBe("Cancelled mid-step.");

        // Somebody pressed the button; telling them the run succeeded would be a lie, and
        // telling them it was cancelled is telling them what they did.
        told.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_taken_over_during_a_step_keeps_the_new_owners_outcome_and_tells_nobody()
    {
        var runId = await SeedRunningRunAsync();

        await using var worker = api.Services.CreateAsyncScope();
        var workersDb = worker.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var told = new RecordingPublisher(workersDb);

        // The sweep, through another context, gives the run up while the step runs — what it
        // does to a run whose lease lapsed on its last attempt — with the step it was on
        // failed for the same reason.
        await ExecuteAsync(
            worker.ServiceProvider, runId,
            new InterferingValidator(api, runId, run => run.Fail(LeaseExpired)), told);

        var stored = await ReadAsync(runId);
        var validate = await ReadStepAsync(runId, StepKind.Validate);

        // The sweep's outcome stands, on the run and on the step: this worker's Succeeded
        // never landed over the Failed the sweep wrote.
        stored.Status.ShouldBe(RunStatus.Failed);
        stored.FailureReason.ShouldBe(LeaseExpired);
        stored.ResolvedVersion.ShouldBeNull();
        validate.Status.ShouldBe(StepStatus.Failed);
        validate.FailureReason.ShouldBe(LeaseExpired);

        // The failure is the sweep's to report, and it did; a second "Run failed" from the
        // worker that lost the run — built from steps it never got to save — is noise, and
        // a "Run succeeded" for a run the sweep failed would be worse.
        told.Published.ShouldBeEmpty();

        // Nothing of this worker's is left pending on its context: the step it succeeded,
        // the spec it marked validated. A later save on this context — the notification's,
        // in the real publisher — would have landed them over what the sweep wrote.
        workersDb.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task A_run_cancelled_during_generation_keeps_the_step_the_cancel_skipped()
    {
        var runId = await SeedRunningRunAsync();

        await using var worker = api.Services.CreateAsyncScope();
        var workersDb = worker.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var told = new RecordingPublisher(workersDb);

        // Past validation, then cancelled while the generator runs. The Generate step's own
        // closing save changes nothing on the run — the token is on the run alone — and used
        // to land Succeeded over the Skipped the cancel had just written on the step.
        await ExecuteAsync(
            worker.ServiceProvider, runId, new AcceptingValidator(), told,
            new InterferingGenerator(api, runId, run => run.Cancel("Cancelled mid-step.")),
            new NoScaffolder());

        var stored = await ReadAsync(runId);
        var generate = await ReadStepAsync(runId, StepKind.Generate);

        stored.Status.ShouldBe(RunStatus.Cancelled);
        generate.Status.ShouldBe(StepStatus.Skipped);
        told.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_claimed_through_raw_sql_can_still_be_written()
    {
        var runId = await SeedQueuedRunAsync();

        // The claim reads the row with a hand-written SELECT. If that SELECT left the token
        // out, the write below would fail for lack of it — or EF would have refused to
        // materialise the row at all.
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var claimed = await scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>()
            .TryClaimAsync(runId, "worker-1", TestContext.Current.CancellationToken);

        claimed.ShouldNotBeNull();
        claimed.RenewLease(DateTimeOffset.UtcNow);

        await Should.NotThrowAsync(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Does something to the run through a second context, while the executor is inside a
    /// step with its own copy — what the API cancelling it, or the sweep taking it back, looks
    /// like from the executor's side, timed to land while the step runs.
    /// </summary>
    private static async Task MeanwhileAsync(
        SpaceCraftApi api, Guid runId, Action<PipelineRun> act, CancellationToken ct)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var run = await db.PipelineRuns.Include(r => r.Steps).SingleAsync(r => r.Id == runId, ct);

        act(run);
        await db.SaveChangesAsync(ct);
    }

    private static SpecValidationResult Fine() =>
        new(SpecValidationStatus.Valid, "Billing API", "1.0.0", "3.0.3", [], OperationCount: 0);

    /// <summary>A validator that finds the document fine.</summary>
    private sealed class AcceptingValidator : ISpecValidator
    {
        public Task<SpecValidationResult> ValidateAsync(
            Stream content, string fileName, bool strict, string? previousApiVersion, CancellationToken ct) =>
            Task.FromResult(Fine());
    }

    /// <summary>
    /// A validator that does something to the run from a second context while it is being
    /// asked about the document, then answers that the document is fine.
    /// </summary>
    private sealed class InterferingValidator(SpaceCraftApi api, Guid runId, Action<PipelineRun> act)
        : ISpecValidator
    {
        public async Task<SpecValidationResult> ValidateAsync(
            Stream content, string fileName, bool strict, string? previousApiVersion, CancellationToken ct)
        {
            await MeanwhileAsync(api, runId, act, ct);
            return Fine();
        }
    }

    /// <summary>
    /// A generator that does something to the run from a second context while it is asked to
    /// generate, then answers that it did.
    /// </summary>
    private sealed class InterferingGenerator(SpaceCraftApi api, Guid runId, Action<PipelineRun> act)
        : ISdkGenerator
    {
        public SdkLanguage Language => SdkLanguage.TypeScript;

        public async Task<GenerationOutcome> GenerateAsync(GenerationRequest request, CancellationToken ct)
        {
            await MeanwhileAsync(api, runId, act, ct);
            return new GenerationOutcome { Succeeded = true, Output = string.Empty };
        }
    }

    /// <summary>A scaffolder with nothing to add: the generated tree is not what is under test.</summary>
    private sealed class NoScaffolder : IPackageScaffolder
    {
        public SdkLanguage Language => SdkLanguage.TypeScript;

        public Task ScaffoldAsync(ScaffoldRequest request, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>
    /// Records what the executor tried to tell people, and saves on the worker's own context
    /// as the real publisher does: that save is how a step the database had refused, still
    /// tracked as modified, used to land after all.
    /// </summary>
    private sealed class RecordingPublisher(SpaceCraftDbContext db) : INotificationPublisher
    {
        public List<NotificationContent> Published { get; } = [];

        public Task PublishToGovernanceAsync(
            Guid organizationId, Guid? actor, NotificationContent content, CancellationToken ct) =>
            RecordAsync(content, ct);

        public Task PublishToMemberAsync(
            Guid organizationId, Guid userId, NotificationContent content, CancellationToken ct) =>
            RecordAsync(content, ct);

        public Task PublishToPersonAsync(
            Guid organizationId, Guid userId, NotificationContent content, CancellationToken ct) =>
            RecordAsync(content, ct);

        private async Task RecordAsync(NotificationContent content, CancellationToken ct)
        {
            Published.Add(content);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// The real executor over the API's own database, on the scope the test hands it — so the
    /// test can look at the worker's context afterwards — with the seams the test needs
    /// replaced. No builders or publishers: no run gets that far.
    /// </summary>
    private static async Task ExecuteAsync(
        IServiceProvider services,
        Guid runId,
        ISpecValidator validator,
        INotificationPublisher notifications,
        ISdkGenerator? generator = null,
        IPackageScaffolder? scaffolder = null)
    {
        var executor = new PipelineExecutor(
            services.GetRequiredService<SpaceCraftDbContext>(),
            services.GetRequiredService<IObjectStorage>(),
            validator,
            generator is null ? [] : [generator],
            scaffolder is null ? [] : [scaffolder],
            [],
            [],
            services.GetRequiredService<ISecretProtector>(),
            notifications,
            Options.Create(new PipelineOptions()),
            NullLogger<PipelineExecutor>.Instance);

        await executor.ExecuteAsync(runId, TestContext.Current.CancellationToken);
    }

    private async Task<Guid> SeedRunningRunAsync()
    {
        var runId = await SeedQueuedRunAsync();

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var run = await db.PipelineRuns.SingleAsync(r => r.Id == runId, TestContext.Current.CancellationToken);

        run.Claim("worker-1", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return runId;
    }

    /// <summary>
    /// A queued run against a real organization, project, target and spec version — the runs
    /// table carries foreign keys to all four — with the document in storage, so the executor
    /// can download it.
    /// </summary>
    private async Task<Guid> SeedQueuedRunAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var slug = $"race-{Guid.CreateVersion7():N}"[..24];
        var organization = new Organization("Race", slug, Guid.CreateVersion7());
        var project = organization.AddProject("Billing", "billing");
        var target = project.AddTarget(SdkLanguage.TypeScript, "billing-sdk", RegistryKind.Npm);
        var spec = project.AddSpecVersion(id => $"specs/{id}.yaml", "hash", Spec.Length, SpecSource.Upload);

        db.Organizations.Add(organization);

        var run = new PipelineRun(
            organization.Id, project.Id, spec.Id, spec.Sequence, target,
            "https://registry.npmjs.org", "npm", RunTrigger.Manual, publishRequested: false,
            triggeredByUserId: Guid.CreateVersion7());

        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(ct);

        await scope.ServiceProvider.GetRequiredService<IObjectStorage>()
            .PutTextAsync(spec.StorageKey, Spec, "application/yaml", ct);

        return run.Id;
    }

    private async Task<PipelineRun> ReadAsync(Guid runId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        return await db.PipelineRuns.AsNoTracking().SingleAsync(r => r.Id == runId, TestContext.Current.CancellationToken);
    }

    private async Task<PipelineStep> ReadStepAsync(Guid runId, StepKind kind)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        return await db.PipelineRuns
            .AsNoTracking()
            .Where(r => r.Id == runId)
            .SelectMany(r => r.Steps)
            .SingleAsync(s => s.Kind == kind, TestContext.Current.CancellationToken);
    }
}
