using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Api.Features.Notifications;

/// <summary>
/// How often notifications are delivered and how long they are kept.
/// </summary>
/// <remarks>
/// These were read one key at a time with a <c>Math.Clamp</c> around each. The ranges below
/// are the same bounds, stated where the options pattern states them, and enforced at startup
/// rather than silently narrowed at the first read — a value outside them is a configuration
/// error and says so.
/// </remarks>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>
    /// How often the dispatcher sweeps for unsent notifications. Long enough that an idle
    /// deployment is not querying constantly, short enough that a failed run reaches a
    /// mailbox while the person who started it still remembers it. Configurable because the
    /// tests assert that a message actually arrives, and waiting a quarter of a minute for
    /// each is a suite nobody runs. Bounded so it cannot become a busy loop.
    /// </summary>
    [Range(1, 300)]
    public int SweepSeconds { get; set; } = 15;

    /// <summary>
    /// How many are claimed per sweep. With the interval, this is the ceiling on delivery
    /// rate, and the first thing to raise if a backlog builds. It is also how much a crash
    /// costs: a batch is stamped as sent before any of it is handed over, so a process that
    /// dies mid-sweep loses at most this many, never a duplicate.
    /// </summary>
    [Range(1, 500)]
    public int BatchSize { get; set; } = 50;

    /// <summary>How often the purge runs.</summary>
    [Range(1, 168)]
    public int PurgeHours { get; set; } = 6;

    /// <summary>How long a notification somebody has read is kept.</summary>
    [Range(1, 3650)]
    public int ReadRetentionDays { get; set; } = 30;

    /// <summary>How long one nobody has read is kept — longer, since it may still be wanted.</summary>
    [Range(1, 3650)]
    public int UnreadRetentionDays { get; set; } = 90;
}
