using Microsoft.Extensions.Options;
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
/// </remarks>
public class PipelineTimeoutTests
{
    private static readonly PipelineOptions Options = new();

    /// <summary>Enough that a step finishing near its limit is not a race with the reaper.</summary>
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    [Fact]
    public void The_lease_outlasts_the_longest_step_it_has_to_cover()
    {
        // Renewed before each step, so the sum does not matter — the longest one does.
        var longest = new[] { Options.GenerateTimeout, Options.BuildTimeout, Options.PublishTimeout }.Max();

        PipelineRun.LeaseDuration.ShouldBeGreaterThan(
            longest + Margin,
            $"the longest step is allowed {longest}, and the lease must outlast it by {Margin}");
    }
}
