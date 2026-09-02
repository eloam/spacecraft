using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SpaceCraft.Pipeline.Sandbox;

/// <summary>
/// Runs commands directly on the host, with no isolation whatsoever.
/// <para>
/// <b>This is the runner everywhere, production included.</b> It is the only
/// <c>ICommandRunner</c> and <c>DependencyInjection.AddSpaceCraftPipeline</c> registers it
/// unconditionally, so there is nothing to select and no sandboxed alternative to select
/// instead: a tenant's generated code runs as a child of the worker's own process, under the
/// worker's own user. It ignores <see cref="CommandRequest.Network"/> — the policy is
/// declared by callers and read by nobody — and offers no filesystem containment. The
/// isolation the design relies on is the job's, not this class's.
/// </para>
/// <para>
/// Which is why the registration itself is the gate. Outside Development,
/// <c>AddSpaceCraftPipeline</c> throws unless <c>Pipeline:AllowUnsandboxedExecution</c> is
/// true, so running this way in production is a decision someone wrote down rather than a
/// default nobody read.
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

        // Emptied first, then filled with what the step actually needs.
        //
        // A child inherited this process's entire environment, and in production that holds
        // the platform's own secrets: the Key Manager token that unwraps the Data Protection
        // ring — and therefore every organization's registry credentials — the object storage
        // keys, and the database connection string. Every one of them was readable by npm, by
        // the generator, and by anything either of them ran.
        //
        // --ignore-scripts was the whole of the defence. It is a good flag and it is one flag:
        // a dependency whose install path executes anyway, a step that forgets it, or a tool
        // that expands ${VAR} in a config file — which npm does, and which this code relies on
        // for the publish token — reads the lot.
        startInfo.Environment.Clear();

        // What a process needs to be a process. PATH so the executable's own helpers resolve,
        // HOME because npm and the generator both write there, and the locale so tools do not
        // fall back to guessing an encoding.
        foreach (var inherited in new[] { "PATH", "HOME", "LANG", "LC_ALL", "TMPDIR" })
        {
            if (Environment.GetEnvironmentVariable(inherited) is { Length: > 0 } value)
            {
                startInfo.Environment[inherited] = value;
            }
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
