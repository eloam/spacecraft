using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// A job is started for one named run and claims it by id. Two jobs can be started for the
/// same run — two sweeps in the same second, a redispatch after a slow boot — and only one of
/// them may execute it.
/// </summary>
/// <remarks>
/// Needs a real Postgres because the claim is Postgres's: a row taken under
/// <c>FOR UPDATE SKIP LOCKED</c> and a status filter, in one statement. What the runner makes
/// of the answer is covered without a database in <c>RunExecutorTests</c>; what the answer
/// is, for the states a run can be in when a job arrives, is covered here.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class RunClaimTests(SpaceCraftApi api)
{
    [Fact]
    public async Task A_job_walks_past_the_run_another_job_is_claiming_and_the_winner_gets_it_whole()
    {
        var ct = TestContext.Current.CancellationToken;
        var runId = await SeedRunAsync(_ => { });

        // Another job, mid-claim: its transaction holds the row under FOR UPDATE and has not
        // committed. Held from a raw connection rather than a DbContext, because the
        // context's retrying execution strategy refuses a transaction it cannot replay — and
        // because the lock is the whole point, not anything EF would do with it. Two claims
        // started with Task.WhenAll would not do: nothing makes them overlap, and when they
        // do, a claim without SKIP LOCKED blocks on the winner's lock, re-evaluates its WHERE
        // once the winner commits, finds the status changed and returns null just the same.
        await using var holder = new NpgsqlConnection(await ConnectionStringAsync());
        await holder.OpenAsync(ct);
        await using var held = await holder.BeginTransactionAsync(ct);

        await using (var lockRow = new NpgsqlCommand(
            "SELECT id FROM spacecraft.pipeline_runs WHERE id = @id FOR UPDATE", holder, held))
        {
            lockRow.Parameters.AddWithValue("id", runId);
            await lockRow.ExecuteNonQueryAsync(ct);
        }

        // Under plain FOR UPDATE this waits for the holder, which is not going to finish
        // inside the timeout. Under SKIP LOCKED the loser has nothing to do, at once.
        (await ClaimAsync(runId, "worker-b").WaitAsync(TimeSpan.FromSeconds(10), ct)).ShouldBeNull();

        await held.RollbackAsync(ct);

        // Released, the row is claimable exactly once, and handed back ready to execute:
        // Running, held by the winner, with its steps loaded — FromSql cannot carry an
        // Include, so the queue loads them afterwards, and a claim that forgot would hand the
        // executor a run it cannot drive.
        var run = (await ClaimAsync(runId, "worker-a")).ShouldNotBeNull();
        run.Status.ShouldBe(RunStatus.Running);
        run.ClaimedBy.ShouldBe("worker-a");
        run.Steps.Count.ShouldBe(4);

        (await ClaimAsync(runId, "worker-b")).ShouldBeNull();

        var stored = await ReadAsync(runId);
        stored.ClaimedBy.ShouldBe("worker-a");
        stored.AttemptCount.ShouldBe(1);
    }

    /// <summary>
    /// A job that arrives for a run somebody else already holds, or that was cancelled while
    /// the job was booting, must find nothing rather than run it.
    /// </summary>
    [Theory]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.Cancelled)]
    public async Task A_run_that_is_no_longer_queued_is_not_claimable(RunStatus status)
    {
        var runId = await SeedRunAsync(run =>
        {
            if (status is RunStatus.Running)
            {
                run.Claim("worker-that-got-there-first", DateTimeOffset.UtcNow);
            }
            else
            {
                run.Cancel("Cancelled before the job started.");
            }
        });

        (await ClaimAsync(runId, "late-worker")).ShouldBeNull();

        // And it was left as it was: a refused claim writes nothing.
        var stored = await ReadAsync(runId);
        stored.Status.ShouldBe(status);
        stored.ClaimedBy.ShouldNotBe("late-worker");
    }

    [Fact]
    public async Task Three_refusals_to_start_a_job_fail_the_run()
    {
        var runId = await SeedRunAsync(_ => { });

        // Each refusal is a sweep of its own, so each goes through a context of its own. The
        // first two leave the run worth retrying; the third is the limit, and the run has to
        // say so rather than be swept for ever.
        (await RecordRefusalAsync(runId, "Job definition not found.")).ShouldBeTrue();
        (await RecordRefusalAsync(runId, "Job definition not found.")).ShouldBeTrue();
        (await RecordRefusalAsync(runId, "Job definition not found.")).ShouldBeFalse();

        var stored = await ReadAsync(runId);
        stored.Status.ShouldBe(RunStatus.Failed);
        stored.DispatchAttempts.ShouldBe(3);
        stored.FailureReason.ShouldNotBeNull();
        stored.FailureReason.ShouldContain("after 3 attempts");
        stored.FailureReason.ShouldContain("Job definition not found.");

        // Failed with its steps settled, not Failed over four steps still Pending: polling
        // stops at a terminal run, so a step left Pending is a spinner that never resolves.
        // The refusal has to load the steps for Fail to reach them.
        stored.Steps.Count.ShouldBe(4);
        stored.Steps.ShouldAllBe(s => s.Status == StepStatus.Skipped);

        // Failed is terminal: a fourth sweep, or a job that boots late, finds nothing.
        (await ClaimAsync(runId, "late-worker")).ShouldBeNull();
    }

    private async Task<string> ConnectionStringAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>().Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test host has no connection string.");
    }

    private async Task<PipelineRun?> ClaimAsync(Guid runId, string workerId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>()
            .TryClaimAsync(runId, workerId, TestContext.Current.CancellationToken);
    }

    private async Task<bool> RecordRefusalAsync(Guid runId, string reason)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>()
            .RecordDispatchRefusalAsync(runId, reason, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A run against a real organization, project, target and spec version — the runs table
    /// carries foreign keys to all four — put in whatever state the test needs before it is
    /// saved.
    /// </summary>
    private async Task<Guid> SeedRunAsync(Action<PipelineRun> arrange)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var slug = $"claim-{Guid.CreateVersion7():N}"[..24];
        var organization = new Organization("Claim", slug, Guid.CreateVersion7());
        var project = organization.AddProject("Billing", "billing");
        var target = project.AddTarget(SdkLanguage.TypeScript, "billing-sdk", RegistryKind.Npm);
        var spec = project.AddSpecVersion(id => $"specs/{id}.yaml", "hash", 10, SpecSource.Upload);

        db.Organizations.Add(organization);

        var run = new PipelineRun(
            organization.Id, project.Id, spec.Id, spec.Sequence, target,
            "https://registry.npmjs.org", "npm", RunTrigger.Manual, publishRequested: false);

        arrange(run);

        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(ct);

        return run.Id;
    }

    private async Task<PipelineRun> ReadAsync(Guid runId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        return await db.PipelineRuns
            .AsNoTracking()
            .Include(r => r.Steps)
            .SingleAsync(r => r.Id == runId, TestContext.Current.CancellationToken);
    }
}
