namespace SpaceCraft.Runner;

/// <summary>
/// The parsed command line: a run to execute, or the reason there is none.
/// </summary>
/// <remarks>
/// Deliberately hand-rolled and tiny. The runner is invoked by a trigger rather than by a
/// person, and it takes one guid — a command-line library would be a dependency in the image
/// for that. Parsing is pure, takes its environment as a parameter, and is unit-tested.
/// <para>
/// There was a <c>RunnerMode</c> here while the runner could drain a queue, reap expired
/// leases, migrate a schema and rewrite stored objects. Each of those went for its own reason,
/// and an enum naming the one that is left would encode nothing.
/// </para>
/// </remarks>
public sealed record RunnerCommand
{
    // Private, so the only ways in are the two factories below and the contradictory state —
    // no run and no error, which IsValid would call valid — cannot be built at all. The
    // alternative was a null-forgiving operator at the call site, which asserts the invariant
    // rather than establishing it, and fails as "Nullable object must have a value" when the
    // assertion is wrong.
    private RunnerCommand(Guid? runId, string? error)
    {
        RunId = runId;
        Error = error;
    }

    public Guid? RunId { get; }

    public string? Error { get; }

    public const string Usage = """
        SpaceCraft.Runner — executes one SDK pipeline run, then exits.

          --run <guid>   the run to execute

        The run id may also be supplied as SPACECRAFT_RUN_ID, which is how some schedulers
        pass per-invocation input. An argument wins over the environment, so a stale variable
        cannot redirect an invocation that said what it wanted.
        """;

    public bool IsValid => Error is null;

    /// <summary>
    /// The run to execute. Throws rather than returning a default when there is none, and says
    /// why — a caller reaching this without checking <see cref="IsValid"/> is a bug, and it
    /// should read as one.
    /// </summary>
    public Guid Run => RunId ?? throw new InvalidOperationException(
        $"This command has no run to execute: {Error ?? "no reason was recorded, which is itself a bug."}");

    public static RunnerCommand Parse(string[] args, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);

        if (args.Length == 0)
        {
            return FromEnvironment(environment);
        }

        return args[0] switch
        {
            "--run" when args.Length < 2 => Invalid("--run requires a run id."),
            "--run" => Parsed(args[1], $"'{args[1]}' is not a valid run id."),
            _ => Invalid($"Unrecognised argument '{args[0]}'."),
        };
    }

    /// <summary>
    /// No arguments: the run id may still arrive in the environment, because schedulers differ
    /// in how they pass per-invocation input.
    /// </summary>
    private static RunnerCommand FromEnvironment(Func<string, string?> environment)
    {
        var value = environment("SPACECRAFT_RUN_ID");

        if (string.IsNullOrWhiteSpace(value))
        {
            // There is no default. Draining the queue used to be one, and it carried several
            // tenants' runs in a single process — the isolation this image relies on is one
            // execution per container, so every invocation has to say which run it is for.
            return Invalid("No run given. Pass --run <id>, or set SPACECRAFT_RUN_ID.");
        }

        return Parsed(value, $"SPACECRAFT_RUN_ID is '{value}', which is not a valid run id.");
    }

    private static RunnerCommand Parsed(string value, string error) =>
        Guid.TryParse(value, out var id) ? ForRun(id) : Invalid(error);

    private static RunnerCommand ForRun(Guid runId) => new(runId, null);

    private static RunnerCommand Invalid(string error) => new(null, error);
}
