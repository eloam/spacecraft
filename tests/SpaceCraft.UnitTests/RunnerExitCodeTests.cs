using SpaceCraft.Runner;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The number the runner hands back to whatever started it.
/// </summary>
/// <remarks>
/// This is the whole interface between the process and its scheduler, and it is the kind of
/// thing that gets "tidied" by someone who reads <c>NothingToDo</c> or a failed run as an error
/// and makes it exit non-zero. Scaleway would then retry the job. The run has an attempt limit,
/// so the retry does not just waste a container — it spends an attempt nobody asked to spend,
/// and three of those fail the run outright.
/// <para>
/// The mapping used to be a local function in <c>Program.cs</c>. Top-level statements compile
/// into a generated class no test project can name, so none of this could be asserted at all;
/// moving it out was the point of the exercise.
/// </para>
/// </remarks>
public class RunnerExitCodeTests
{
    [Fact]
    public void A_completed_invocation_exits_zero()
    {
        // "Completed" means the invocation did its job. It says nothing about whether the run
        // succeeded — a run that failed on its own merits is recorded as failed and still lands
        // here, which is exactly the case that must not look like an error to the scheduler.
        RunnerOutcome.Completed.ExitCode().ShouldBe(RunnerExitCode.Success);
    }

    [Fact]
    public void Finding_nothing_to_do_exits_zero()
    {
        // Two sweeps can start a job for the same run; the second finds it already claimed.
        // Ordinary, and the container that lost the race must not report a fault for it.
        RunnerOutcome.NothingToDo.ExitCode().ShouldBe(RunnerExitCode.Success);
    }

    [Fact]
    public void A_faulted_invocation_exits_non_zero()
    {
        RunnerOutcome.Faulted.ExitCode().ShouldNotBe(RunnerExitCode.Success);
    }

    [Fact]
    public void An_outcome_nobody_thought_about_fails_rather_than_passing_silently()
    {
        // A member added to RunnerOutcome without a thought for this mapping falls to the
        // discard. It has to land on a failure: an unconsidered outcome reported as success is
        // a run that disappears with nobody told, which is the worse of the two mistakes.
        var unmapped = (RunnerOutcome)999;

        unmapped.ExitCode().ShouldBe(RunnerExitCode.Faulted);
    }

    [Fact]
    public void Success_is_actually_zero()
    {
        // Asserted on the enum rather than taken on trust from its declaration order. Every
        // scheduler on earth reads a process's success as the literal 0, so this value is not
        // free to move even though nothing in C# would complain if it did.
        ((int)RunnerExitCode.Success).ShouldBe(0);
    }

    [Fact]
    public void A_mis_invoked_runner_is_distinguishable_from_a_broken_one()
    {
        // Both are non-zero, and they have to stay different numbers: bad arguments mean a bad
        // job definition and retrying will never help, while a fault may well be transient.
        // Whoever eventually configures retries needs to be able to tell them apart.
        ((int)RunnerExitCode.InvalidArguments).ShouldNotBe(0);
        RunnerExitCode.InvalidArguments.ShouldNotBe(RunnerExitCode.Faulted);
    }
}
