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
/// Several API instances sweep the runs table for work to hand to a job, and none of them may
/// hand out the same run twice.
/// </summary>
/// <remarks>
/// This needs a real Postgres, because the property being tested is Postgres's:
/// <c>FOR UPDATE SKIP LOCKED</c> is what makes two concurrent sweeps share the backlog rather
/// than race for the front of it. An in-memory provider would pass this suite and prove nothing.
/// <para>
/// Duplicating a dispatch is not a correctness fault — claiming a run for execution filters on
/// <see cref="RunStatus.Queued"/> under the same lock, so a second job would find nothing and
/// exit. It is a cost fault: Scaleway allows ten parallel job runs, and three instances each
/// starting a job for one run spends three of them to do one build.
/// </para>
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class DispatchClaimTests(SpaceCraftApi api)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task A_sweep_walks_past_a_row_another_sweep_is_holding()
    {
        var ct = TestContext.Current.CancellationToken;
        var runIds = await SeedQueuedRunsAsync(6);

        // Another sweep, mid-claim: its transaction holds the first row under FOR UPDATE and
        // has not committed. Held from a raw connection rather than a DbContext, because the
        // context's retrying execution strategy refuses a transaction it cannot replay — and
        // because the lock is the whole point, not anything EF would do with it.
        //
        // Two sweeps started with Task.WhenAll used to stand in for this, and proved nothing:
        // nothing guaranteed they overlapped, and when they did, a claim without SKIP LOCKED
        // would block on the winner's lock, re-evaluate its WHERE once the winner committed —
        // READ COMMITTED does that — find the rows dispatched, and return nothing. The same
        // "no duplicates" either way.
        await using var holder = new NpgsqlConnection(await ConnectionStringAsync());
        await holder.OpenAsync(ct);
        await using var held = await holder.BeginTransactionAsync(ct);

        await using (var lockRow = new NpgsqlCommand(
            "SELECT id FROM spacecraft.pipeline_runs WHERE id = @id FOR UPDATE", holder, held))
        {
            lockRow.Parameters.AddWithValue("id", runIds[0]);
            await lockRow.ExecuteNonQueryAsync(ct);
        }

        // Under plain FOR UPDATE this waits for the holder, which is not going to finish
        // inside the timeout. Under SKIP LOCKED it returns the other five at once, in order.
        var claimed = await ClaimAsync(batchSize: 6).WaitAsync(TimeSpan.FromSeconds(10), ct);
        claimed.ShouldBe(runIds.Skip(1).ToList());

        await held.RollbackAsync(ct);

        // Released, the held row is an ordinary queued run again and the next sweep takes it;
        // the five already handed out are inside their window and left alone.
        (await ClaimAsync(batchSize: 6)).ShouldBe([runIds[0]]);
    }

    [Fact]
    public async Task A_dispatched_run_is_not_handed_out_again_inside_the_window()
    {
        await SeedQueuedRunsAsync(1);

        (await ClaimAsync(batchSize: 10)).Count.ShouldBe(1);

        // Still queued — a dispatch is not a claim, because the job may never boot. But it has
        // been asked for, so the next sweep leaves it alone until the window passes.
        (await ClaimAsync(batchSize: 10)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_whose_job_never_started_is_offered_again_at_once()
    {
        await SeedQueuedRunsAsync(1);

        var claimed = await ClaimAsync(batchSize: 10);
        claimed.Count.ShouldBe(1);

        // What the dispatcher does when the platform refuses: quota reached, or unreachable.
        // Nothing was started, so the stamp comes off rather than costing the run the window.
        await using (var scope = api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>()
                .ReleaseDispatchAsync(claimed, TestContext.Current.CancellationToken);
        }

        (await ClaimAsync(batchSize: 10)).ShouldBe(claimed);
    }

    [Fact]
    public async Task A_sweep_takes_no_more_than_its_batch_and_takes_the_oldest_first()
    {
        var runIds = await SeedQueuedRunsAsync(5);

        var claimed = await ClaimAsync(batchSize: 2);

        // Ordered by queued_at, so a backlog drains in the order people queued it rather than
        // in whatever order the index happens to return.
        claimed.ShouldBe(runIds.Take(2).ToList());
    }

    [Fact]
    public async Task A_run_whose_worker_died_is_picked_up_without_a_reaper()
    {
        var runIds = await SeedQueuedRunsAsync(1);

        // Claimed by a worker that then stopped existing. The lease is what says so, and it
        // has lapsed — there is no separate reaper any more, so the sweep has to see it.
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
            var run = await db.PipelineRuns.SingleAsync(r => r.Id == runIds[0], TestContext.Current.CancellationToken);
            run.Claim("worker-that-died", DateTimeOffset.UtcNow - PipelineRun.LeaseDuration - TimeSpan.FromMinutes(1));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await ClaimAsync(batchSize: 10)).ShouldBe(runIds);

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
            var run = await db.PipelineRuns.SingleAsync(r => r.Id == runIds[0], TestContext.Current.CancellationToken);

            // Back to queued and stamped for dispatch, with the attempt that died still counted
            // against the limit — a run that kills its worker three times has to stop, not loop.
            run.Status.ShouldBe(RunStatus.Queued);
            run.ClaimedBy.ShouldBeNull();
            run.LeaseExpiresAt.ShouldBeNull();
            run.DispatchedAt.ShouldNotBeNull();
            run.AttemptCount.ShouldBe(1);
        }
    }

    [Fact]
    public async Task A_run_that_has_killed_its_worker_too_often_fails_instead_of_being_handed_out()
    {
        var runIds = await SeedQueuedRunsAsync(1);

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
            var run = await db.PipelineRuns.SingleAsync(r => r.Id == runIds[0], TestContext.Current.CancellationToken);
            var expired = DateTimeOffset.UtcNow - PipelineRun.LeaseDuration - TimeSpan.FromMinutes(1);

            // Three claims, three deaths. The third is the limit.
            for (var i = 0; i < 3; i++)
            {
                run.Claim($"worker-{i}", expired);
                if (i < 2)
                {
                    run.TryRequeue("died");
                }
            }

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Not dispatchable: the sweep fails it rather than starting a fourth job for it.
        (await ClaimAsync(batchSize: 10)).ShouldBeEmpty();

        await using (var verify = api.Services.CreateAsyncScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
            var run = await db.PipelineRuns.SingleAsync(r => r.Id == runIds[0], TestContext.Current.CancellationToken);

            run.Status.ShouldBe(RunStatus.Failed);
            run.FailureReason.ShouldNotBeNull().ShouldContain("gave up after 3 attempts");
        }
    }

    private async Task<string> ConnectionStringAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>().Database.GetConnectionString()
            ?? throw new InvalidOperationException("The test host has no connection string.");
    }

    private async Task<IReadOnlyList<Guid>> ClaimAsync(int batchSize)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>()
            .ClaimForDispatchAsync(batchSize, Window, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Queues runs against a real organization, project, target and spec version, because the
    /// runs table carries foreign keys to all four. Returns their ids oldest first.
    /// </summary>
    private async Task<List<Guid>> SeedQueuedRunsAsync(int count)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        // Every other run in the table would otherwise be a candidate for these sweeps, and
        // the suite shares one database. Emptying it keeps each case reasoning about its own.
        await db.PipelineSteps.ExecuteDeleteAsync(ct);
        await db.PipelineRuns.ExecuteDeleteAsync(ct);

        var slug = $"dispatch-{Guid.CreateVersion7():N}"[..24];
        var organization = new Organization("Dispatch", slug, Guid.CreateVersion7());
        var project = organization.AddProject("Billing", "billing");
        var target = project.AddTarget(SdkLanguage.TypeScript, "billing-sdk", RegistryKind.Npm);
        var spec = project.AddSpecVersion(id => $"specs/{id}.yaml", "hash", 10, SpecSource.Upload);

        db.Organizations.Add(organization);
        await db.SaveChangesAsync(ct);

        var runs = new List<PipelineRun>();
        for (var i = 0; i < count; i++)
        {
            runs.Add(new PipelineRun(
                organization.Id, project.Id, spec.Id, spec.Sequence, target,
                "https://registry.npmjs.org", "npm", RunTrigger.Manual, publishRequested: false));
        }

        db.PipelineRuns.AddRange(runs);
        await db.SaveChangesAsync(ct);

        // Ordered here rather than trusted from insertion, and by the same total order the
        // sweep uses. Seeding a handful of runs in a loop puts several in one millisecond, so
        // queued_at alone is not an order — which is exactly how this test failed one run in
        // three before the tiebreak existed.
        return await db.PipelineRuns
            .OrderBy(r => r.QueuedAt)
            .ThenBy(r => r.Id)
            .Select(r => r.Id)
            .ToListAsync(ct);
    }
}
