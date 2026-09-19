using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpaceCraft.Pipeline;
using SpaceCraft.Pipeline.Building;
using SpaceCraft.Pipeline.Sandbox;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The build step runs three commands, and the lease on the run covers the step, not the
/// command. These pin the three to one budget between them.
/// </summary>
/// <remarks>
/// Each command used to be allowed the full BuildTimeout on its own — fifteen, fifteen and
/// two minutes, thirty-two in all under a twenty-five minute lease. A slow install followed
/// by a slow compile was still within every command's limit when the sweep declared the
/// worker dead and handed the run to a second one. PipelineTimeoutTests compares the lease
/// with BuildTimeout; this is what makes BuildTimeout the whole step's number.
/// </remarks>
public class NpmBuildBudgetTests
{
    [Fact]
    public async Task Each_command_gets_what_the_ones_before_it_left()
    {
        var runner = new ScriptedRunner(
            Ran(TimeSpan.FromMinutes(9)),
            Ran(TimeSpan.FromMinutes(4)),
            Ran(TimeSpan.FromSeconds(10), "acme-billing-sdk-1.0.0.tgz"));

        var outcome = await Build(runner);

        outcome.Succeeded.ShouldBeTrue();
        runner.Requests.Count.ShouldBe(3);

        // Install has the whole budget; compile what install left; pack what both left.
        runner.Requests[0].Timeout.ShouldBe(PipelineOptions.BuildTimeout);
        runner.Requests[1].Timeout.ShouldBe(PipelineOptions.BuildTimeout - TimeSpan.FromMinutes(9));
        runner.Requests[2].Timeout.ShouldBe(PipelineOptions.BuildTimeout - TimeSpan.FromMinutes(13));
    }

    [Fact]
    public async Task A_command_that_starts_with_nothing_left_gets_nothing()
    {
        // Install used every minute the step had. Compile must not be given fifteen more.
        var runner = new ScriptedRunner(
            Ran(PipelineOptions.BuildTimeout),
            TimedOut());

        var outcome = await Build(runner);

        runner.Requests[1].Timeout.ShouldBe(TimeSpan.Zero);
        outcome.Succeeded.ShouldBeFalse();

        // Which command, and that it was the step's budget rather than its own that ran out —
        // "timed out after 0 minutes" would read as a bug in the runner.
        outcome.FailureReason.ShouldNotBeNull();
        outcome.FailureReason.ShouldStartWith("Compilation ran out of time");
        outcome.FailureReason.ShouldContain($"{PipelineOptions.BuildTimeout.TotalMinutes:0.#} minutes in all");
    }

    [Fact]
    public async Task Packaging_is_held_to_the_same_budget_as_the_rest()
    {
        // It had a fixed two minutes of its own before, outside the step's budget.
        var runner = new ScriptedRunner(
            Ran(TimeSpan.FromMinutes(7)),
            Ran(TimeSpan.FromMinutes(7)),
            TimedOut());

        var outcome = await Build(runner);

        runner.Requests[2].Timeout.ShouldBe(TimeSpan.FromMinutes(1));
        outcome.FailureReason.ShouldNotBeNull().ShouldStartWith("Packaging ran out of time");
    }

    private static Task<BuildOutcome> Build(ICommandRunner runner) =>
        new NpmSdkBuilder(runner, Options.Create(new PipelineOptions()), NullLogger<NpmSdkBuilder>.Instance)
            .BuildAsync(new BuildRequest
            {
                WorkspaceRoot = "/workspace",
                SourceDirectory = "source",
                ArtifactDirectory = "artifact",
                PackageName = "@acme/billing-sdk",
                Version = "1.0.0",
            }, TestContext.Current.CancellationToken);

    private static CommandResult Ran(TimeSpan took, string output = "") =>
        new() { ExitCode = 0, Output = output, Duration = took };

    private static CommandResult TimedOut() =>
        new() { ExitCode = -1, Output = string.Empty, Duration = TimeSpan.Zero, TimedOut = true };

    /// <summary>Answers each command with the next scripted result and keeps what it was asked.</summary>
    private sealed class ScriptedRunner(params CommandResult[] results) : ICommandRunner
    {
        public List<CommandRequest> Requests { get; } = [];

        public Task<CommandResult> RunAsync(CommandRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(results[Requests.Count - 1]);
        }
    }
}
