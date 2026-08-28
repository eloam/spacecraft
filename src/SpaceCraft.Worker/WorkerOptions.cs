using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Worker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>
    /// Runs executed concurrently by this process. Each one holds a container, so this is
    /// really a statement about how much CPU and memory the host can spare.
    /// </summary>
    [Range(1, 64)]
    public int Concurrency { get; set; } = 2;

    /// <summary>How long to wait before re-checking an empty queue.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Backoff ceiling once the queue has been empty for a while.</summary>
    public TimeSpan MaxPollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often to look for runs abandoned by a dead worker.</summary>
    public TimeSpan ReaperInterval { get; set; } = TimeSpan.FromMinutes(1);
}
