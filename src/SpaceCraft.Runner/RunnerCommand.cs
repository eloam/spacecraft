namespace SpaceCraft.Runner;

/// <summary>What a single invocation of the runner should do before exiting.</summary>
public enum RunnerMode
{
    /// <summary>Execute one named run. The shape a per-run trigger uses.</summary>
    OneRun,

    /// <summary>Claim and execute queued runs until the queue is empty or the budget is spent.</summary>
    Drain,

    /// <summary>Return abandoned runs to the queue and exit. The shape a cron trigger uses.</summary>
    Reap,

    /// <summary>Apply pending database migrations and exit. Runs as a deployment step.</summary>
    Migrate,

    /// <summary>
    /// Move stored objects to the current key layout and exit. A one-off after the layout
    /// changes, not part of any schedule.
    /// </summary>
    RelocateStorage,
}

/// <summary>
/// The parsed command line.
/// <para>
/// Deliberately hand-rolled and tiny: the runner is invoked by a scheduler or a trigger, not
/// by a person, so a command-line library would be a dependency in the image for three flags.
/// Parsing is pure, takes its environment as a parameter, and is unit-tested.
/// </para>
/// </summary>
public sealed record RunnerCommand(
    RunnerMode Mode,
    Guid? RunId = null,
    string? Error = null,
    bool DryRun = false)
{
    public const string Usage = """
        SpaceCraft.Runner — executes SDK pipeline runs, then exits.

          --run <guid>   execute one specific queued run
          --drain        execute queued runs until the queue is empty (default)
          --reap         requeue runs abandoned by a dead executor, then exit
          --migrate      apply pending database migrations, then exit

          --relocate-storage   move stored objects to the current key layout, then exit
                               add --dry-run to print the moves without making them

        The run id may also be supplied as SPACECRAFT_RUN_ID, which is how most schedulers
        pass per-invocation input.
        """;

    public bool IsValid => Error is null;

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
            "--reap" => new RunnerCommand(RunnerMode.Reap),
            "--relocate-storage" => new RunnerCommand(
                RunnerMode.RelocateStorage,
                // Position-independent: --dry-run guards a destructive command, and having it
                // silently ignored because it came in the wrong order is not a failure mode
                // worth having.
                DryRun: args.Contains("--dry-run", StringComparer.Ordinal)),
            "--migrate" => new RunnerCommand(RunnerMode.Migrate),
            "--drain" => new RunnerCommand(RunnerMode.Drain),
            "--run" when args.Length < 2 => Invalid("--run requires a run id."),
            "--run" => Guid.TryParse(args[1], out var id)
                ? new RunnerCommand(RunnerMode.OneRun, id)
                : Invalid($"'{args[1]}' is not a valid run id."),
            _ => Invalid($"Unrecognised argument '{args[0]}'."),
        };
    }

    /// <summary>
    /// No flags: a run id in the environment means a per-run trigger, otherwise drain the
    /// queue. Schedulers vary in whether they pass input as arguments or environment.
    /// </summary>
    private static RunnerCommand FromEnvironment(Func<string, string?> environment)
    {
        var value = environment("SPACECRAFT_RUN_ID");

        if (string.IsNullOrWhiteSpace(value))
        {
            return new RunnerCommand(RunnerMode.Drain);
        }

        return Guid.TryParse(value, out var id)
            ? new RunnerCommand(RunnerMode.OneRun, id)
            : Invalid($"SPACECRAFT_RUN_ID is '{value}', which is not a valid run id.");
    }

    private static RunnerCommand Invalid(string error) => new(RunnerMode.Drain, null, error);
}
