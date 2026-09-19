using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Api.Extensions;

/// <summary>
/// How much traffic a caller gets before the API refuses more.
/// </summary>
/// <remarks>
/// Configurable so a load test or a staging soak is not throttled into meaninglessness, and so
/// the limits can be tightened in production without a rebuild. Every value has a default a
/// deployment can run on; the ranges are what keeps a typo from turning the limiter off or
/// into a wall.
/// </remarks>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>
    /// Baseline for authenticated traffic, per user per minute — partitioned so one tenant
    /// cannot exhaust another's budget.
    /// </summary>
    [Range(1, 100_000)]
    public int GlobalPermitsPerMinute { get; set; } = 300;

    /// <summary>
    /// Attempts an address gets on each unauthenticated credential endpoint — sign-in,
    /// registration, password reset, waitlist — inside <see cref="CredentialWindow"/>.
    /// </summary>
    // The ceiling is deliberately generous: the integration host sets this to ten thousand
    // to take the limiter out of the way, and a range that refused that would refuse the tests.
    [Range(1, 100_000)]
    public int CredentialPermitsPerWindow { get; set; } = 10;

    /// <summary>The window those attempts are counted over.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan CredentialWindow { get; set; } = TimeSpan.FromMinutes(15);
}
