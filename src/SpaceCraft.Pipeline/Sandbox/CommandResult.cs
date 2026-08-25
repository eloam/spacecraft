namespace SpaceCraft.Pipeline.Sandbox;

public sealed record CommandResult
{
    public required int ExitCode { get; init; }

    /// <summary>Combined stdout and stderr, already redacted.</summary>
    public required string Output { get; init; }

    public required TimeSpan Duration { get; init; }

    public bool TimedOut { get; init; }

    public bool Succeeded => ExitCode == 0 && !TimedOut;

    /// <summary>Last few lines of output, for a failure message that fits in a database column.</summary>
    public string Tail(int lines = 20)
    {
        var all = Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('\n', all.TakeLast(lines)).Trim();
    }
}

public interface ICommandRunner
{
    /// <summary>
    /// Runs a command to completion. A non-zero exit code is a normal result, not an exception;
    /// only infrastructure failures throw.
    /// </summary>
    Task<CommandResult> RunAsync(CommandRequest request, CancellationToken ct = default);
}
