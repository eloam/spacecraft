using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Pipeline;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The lease has to outlast the step it covers.
/// </summary>
/// <remarks>
/// These two numbers live in different projects and drifted into each other: the lease was
/// exactly as long as the build step was allowed to take, so a build that used its budget was
/// declared abandoned mid-flight and executed a second time by another worker. Nothing
/// connected them, so nothing objected. This is that connection.
/// <para>
/// A step, not a command. The lease is renewed when a step starts and not again until the
/// next one, so what it must cover is everything a step can do before it ends — and the build
/// step runs three commands. Comparing the lease with the longest single command let each of
/// the three take fifteen minutes under a twenty-five minute lease.
/// </para>
/// </remarks>
public class PipelineTimeoutTests
{
    /// <summary>Enough that a step finishing near its limit is not a race with the reaper.</summary>
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The most each step can take from its lease renewal to its end. Validate runs no
    /// command and has no budget. Generate is one in-process budget. Build is three commands
    /// under one budget — NpmBuildBudgetTests is what makes BuildTimeout the whole step's
    /// number rather than each command's. Publish is one command.
    /// </summary>
    public static TheoryData<string, TimeSpan> StepTotals => new()
    {
        { "Generate", PipelineOptions.GenerateTimeout },
        { "Build", PipelineOptions.BuildTimeout },
        { "Publish", PipelineOptions.PublishTimeout },
    };

    [Theory]
    [MemberData(nameof(StepTotals))]
    public void The_lease_outlasts_everything_a_step_can_do_before_it_ends(string step, TimeSpan total)
    {
        PipelineRun.LeaseDuration.ShouldBeGreaterThan(
            total + Margin,
            $"the {step} step is allowed {total} in all, and the lease must outlast it by {Margin}");
    }
}
