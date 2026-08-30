using Xunit;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using Shouldly;

namespace SpaceCraft.UnitTests;

public class PipelineRunTests
{
    /// <summary>
    /// A real target, because a run copies its settings at construction and there is no
    /// other way in: SdkTarget's constructor is internal, so it comes from a project, which
    /// comes from an organization.
    /// </summary>
    private static SdkTarget NewTarget() =>
        new Organization("Acme", "acme", Guid.CreateVersion7())
            .AddProject("Billing", "billing")
            .AddTarget(SdkLanguage.TypeScript, "billing-sdk", RegistryKind.Npm);

    private static PipelineRun NewRun(bool publish = true) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), NewTarget(),
            "https://registry.npmjs.org", "npm", RunTrigger.Manual, publish);

    [Fact]
    public void A_new_run_is_queued_with_all_four_steps()
    {
        var run = NewRun();

        run.Status.ShouldBe(RunStatus.Queued);
        run.OrderedSteps().Select(s => s.Kind).ShouldBe(
            [StepKind.Validate, StepKind.Generate, StepKind.Build, StepKind.Publish]);
        run.OrderedSteps().ShouldAllBe(s => s.Status == StepStatus.Pending);
    }

    [Fact]
    public void Publish_is_skipped_up_front_when_not_requested()
    {
        var run = NewRun(publish: false);

        run.Step(StepKind.Publish).Status.ShouldBe(StepStatus.Skipped);
    }

    [Fact]
    public void Claiming_takes_a_lease_and_counts_the_attempt()
    {
        var run = NewRun();
        var now = DateTimeOffset.UtcNow;

        run.Claim("worker-1", now);

        run.Status.ShouldBe(RunStatus.Running);
        run.ClaimedBy.ShouldBe("worker-1");
        run.AttemptCount.ShouldBe(1);
        run.LeaseExpiresAt.ShouldBe(now + PipelineRun.LeaseDuration);
    }

    [Fact]
    public void A_running_run_cannot_be_claimed_again()
    {
        var run = NewRun();
        run.Claim("worker-1", DateTimeOffset.UtcNow);

        Should.Throw<InvalidOperationException>(() => run.Claim("worker-2", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Requeue_returns_the_run_to_the_queue_and_clears_the_lease()
    {
        var run = NewRun();
        run.Claim("worker-1", DateTimeOffset.UtcNow);
        run.Step(StepKind.Validate).Start();

        var requeued = run.TryRequeue("lease expired");

        requeued.ShouldBeTrue();
        run.Status.ShouldBe(RunStatus.Queued);
        run.ClaimedBy.ShouldBeNull();
        run.LeaseExpiresAt.ShouldBeNull();
        // The step that was mid-flight is recorded as failed rather than left running.
        run.Step(StepKind.Validate).Status.ShouldBe(StepStatus.Failed);
    }

    [Fact]
    public void Requeue_gives_up_once_attempts_are_exhausted()
    {
        var run = NewRun();

        for (var attempt = 0; attempt < PipelineRun.DefaultMaxAttempts; attempt++)
        {
            run.Claim($"worker-{attempt}", DateTimeOffset.UtcNow);
            if (attempt < PipelineRun.DefaultMaxAttempts - 1)
            {
                run.TryRequeue("transient").ShouldBeTrue();
            }
        }

        run.TryRequeue("still failing").ShouldBeFalse();
        run.Status.ShouldBe(RunStatus.Failed);
        run.FailureReason.ShouldNotBeNull().ShouldContain("gave up after");
    }

    [Fact]
    public void Failing_a_run_skips_the_steps_that_never_ran()
    {
        var run = NewRun();
        run.Claim("worker-1", DateTimeOffset.UtcNow);
        run.Step(StepKind.Validate).Succeed();

        run.Fail("generation blew up");

        run.Status.ShouldBe(RunStatus.Failed);
        run.Step(StepKind.Generate).Status.ShouldBe(StepStatus.Skipped);
        run.Step(StepKind.Build).Status.ShouldBe(StepStatus.Skipped);
        run.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public void A_finished_run_cannot_be_cancelled()
    {
        var run = NewRun();
        run.Claim("worker-1", DateTimeOffset.UtcNow);
        run.Succeed();

        Should.Throw<InvalidOperationException>(() => run.Cancel("too late"));
    }

    [Fact]
    public void Renewing_the_lease_only_makes_sense_while_running()
    {
        var run = NewRun();

        Should.Throw<InvalidOperationException>(() => run.RenewLease(DateTimeOffset.UtcNow));
    }
}
