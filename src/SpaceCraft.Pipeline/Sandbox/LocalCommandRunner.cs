using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SpaceCraft.Pipeline.Sandbox;

/// <summary>
/// Runs commands directly on the host, with no isolation whatsoever.
/// <para>
/// <b>Development and tests only.</b> It ignores <see cref="CommandRequest.Network"/>,
/// the memory and CPU limits, and offers no filesystem containment. Selecting it in
/// production would mean executing untrusted generated code as the worker's own user;
/// <c>PipelineOptions.Validate</c> refuses that combination at startup.
/// </para>
/// </summary>
public sealed class LocalCommandRunner(ILogger<LocalCommandRunner> logger) : ICommandRunner
{
    public async Task<CommandResult> RunAsync(CommandRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workingDirectory = string.IsNullOrEmpty(request.WorkingSubdirectory)
            ? request.WorkingDirectory
            : Path.Combine(request.WorkingDirectory, request.WorkingSubdirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.Executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in request.Environment)
        {
            startInfo.Environment[key] = value;
        }

        logger.LogDebug("Running {Executable} in {WorkingDirectory}", request.Executable, workingDirectory);

        return await ProcessRunner.RunAsync(startInfo, request, ct);
    }
}

/// <summary>Shared process plumbing: stream capture, timeout, redaction.</summary>
internal static class ProcessRunner
{
    public static async Task<CommandResult> RunAsync(
        ProcessStartInfo startInfo,
        CommandRequest request,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var output = new StringBuilder();

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        // Lock on the builder: stdout and stderr are delivered on separate threads.
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (output) { output.AppendLine(e.Data); }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (output) { output.AppendLine(e.Data); }
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start '{startInfo.FileName}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(request.Timeout);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !ct.IsCancellationRequested;
            TryKill(process);

            if (!timedOut)
            {
                throw;
            }
        }

        stopwatch.Stop();

        string captured;
        lock (output) { captured = output.ToString(); }

        if (timedOut)
        {
            captured += $"\n[spacecraft] command exceeded its {request.Timeout.TotalMinutes:0.#} minute budget and was terminated.\n";
        }

        return new CommandResult
        {
            ExitCode = timedOut ? -1 : process.ExitCode,
            Output = OutputRedactor.Redact(captured, request.SecretsToRedact),
            Duration = stopwatch.Elapsed,
            TimedOut = timedOut,
        };
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone between the check and the kill.
        }
    }
}
