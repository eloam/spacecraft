namespace SpaceCraft.Pipeline.Sandbox;

/// <summary>
/// What a step is meant to reach over the network.
///
/// <para>
/// Declarative only: steps share the job's network, so nothing enforces this. It is kept
/// because it records intent — the day steps move to one container each, it is what says
/// which of them may have egress.
/// </para>
/// </summary>
public enum NetworkPolicy
{
    /// <summary>
    /// What the step is meant to reach: nothing. The default, and what generation and build
    /// are declared under.
    /// </summary>
    /// <remarks>
    /// It is a declaration, not a restriction — see the note on the enum. This used to read
    /// "a generated package's install scripts get no opportunity to phone home", which reads
    /// as a protection and is not one: steps share the job's network, so a build reaches
    /// whatever the job reaches. What actually keeps install scripts from running is
    /// <c>--ignore-scripts</c> on every npm invocation, and what keeps them from finding the
    /// platform's secrets is that the child environment is now emptied before it is filled.
    /// </remarks>
    None = 0,

    /// <summary>
    /// Egress permitted, needed to reach a package registry. Only the publish step and
    /// dependency installation use this.
    /// </summary>
    Registry = 1,
}

/// <summary>
/// One command to run.
/// </summary>
public sealed record CommandRequest
{
    public required string Executable { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>The run's workspace, as an absolute path on the host.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Directory the command actually runs in, relative to <see cref="WorkingDirectory"/>.
    /// The whole workspace stays reachable, so a step can still reach sibling directories
    /// (build writes its tarball into ../artifact) while running inside the package dir.
    /// </summary>
    public string? WorkingSubdirectory { get; init; }

    public NetworkPolicy Network { get; init; } = NetworkPolicy.None;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Environment variables for the command. Values here may include registry tokens, which
    /// is why they travel in the environment rather than on the command line: arguments are
    /// visible to every user through the process list, the environment only to the process
    /// itself and to the user it runs as.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Substrings scrubbed from captured output before it is logged or stored. Every secret
    /// passed through <see cref="Environment"/> must also appear here.
    /// </summary>
    public IReadOnlyList<string> SecretsToRedact { get; init; } = [];
}
