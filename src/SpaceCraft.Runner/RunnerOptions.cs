using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Runner;

public sealed class RunnerOptions
{
    public const string SectionName = "Runner";

    /// <summary>
    /// Runs executed concurrently while draining. One is right when each invocation is a
    /// single-purpose job sized for one run; raise it only when the host has the headroom.
    /// </summary>
    [Range(1, 64)]
    public int Concurrency { get; set; } = 1;

    /// <summary>
    /// Most runs a single drain invocation will execute. Bounds how long one invocation can
    /// last so a scheduler's own timeout is never the thing that stops it mid-build.
    /// </summary>
    [Range(1, 10_000)]
    public int MaxRunsPerInvocation { get; set; } = 25;

    /// <summary>
    /// Wall-clock budget for a drain invocation. Once spent, the runner stops claiming new
    /// work and exits cleanly; whatever is left stays queued for the next invocation.
    /// </summary>
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long to keep re-checking an empty queue before concluding there is no work. Zero
    /// exits immediately, which is what a per-run trigger wants; a few seconds absorbs the
    /// gap between a run being committed and the trigger firing.
    /// </summary>
    public TimeSpan EmptyQueueGrace { get; set; } = TimeSpan.FromSeconds(2);
}
