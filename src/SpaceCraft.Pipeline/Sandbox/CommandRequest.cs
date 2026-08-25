namespace SpaceCraft.Pipeline.Sandbox;

/// <summary>
/// What a sandboxed step may reach over the network.
/// </summary>
public enum NetworkPolicy
{
    /// <summary>
    /// No egress at all. The default, and what generation and build run under: a generated
    /// package's install scripts get no opportunity to phone home or exfiltrate anything.
    /// </summary>
    None = 0,

    /// <summary>
    /// Egress permitted, needed to reach a package registry. Only the publish step and
    /// dependency installation use this.
    /// </summary>
    Registry = 1,
}

/// <summary>
/// One command to run under isolation.
/// </summary>
public sealed record CommandRequest
{
    public required string Executable { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>Host directory bind-mounted into the sandbox as /workspace.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Directory the command actually runs in, relative to <see cref="WorkingDirectory"/>.
    /// The whole workspace stays mounted, so a step can still reach sibling directories
    /// (build writes its tarball into ../artifact) while running inside the package dir.
    /// </summary>
    public string? WorkingSubdirectory { get; init; }

    /// <summary>
    /// Container image providing the toolchain. Ignored by the local runner.
    /// </summary>
    public string? Image { get; init; }

    public NetworkPolicy Network { get; init; } = NetworkPolicy.None;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Environment variables for the command. Values here may include registry tokens, so
    /// they are passed via an env file rather than the command line — arguments are visible
    /// in the host process list, environment of a container is not.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Substrings scrubbed from captured output before it is logged or stored. Every secret
    /// passed through <see cref="Environment"/> must also appear here.
    /// </summary>
    public IReadOnlyList<string> SecretsToRedact { get; init; } = [];

    public long MemoryLimitBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    public double CpuLimit { get; init; } = 2.0;
}
