using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace SpaceCraft.Pipeline.Sandbox;

/// <summary>
/// Runs each step in a throwaway container.
/// <para>
/// This is the containment boundary for untrusted code. Generating an SDK is safe enough,
/// but <c>npm install</c> on the generated package executes arbitrary lifecycle scripts
/// written by whoever authored its dependencies. Every hardening flag below exists to make
/// that a non-event: no capabilities, no privilege escalation, read-only root filesystem,
/// a non-root user, bounded CPU/memory/PIDs, and — for everything except publish — no
/// network at all.
/// </para>
/// </summary>
public sealed class DockerCommandRunner(ILogger<DockerCommandRunner> logger) : ICommandRunner
{
    public async Task<CommandResult> RunAsync(CommandRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Image))
        {
            throw new ArgumentException("An image is required when running under Docker.", nameof(request));
        }

        // Secrets go in a 0600 env file rather than on the command line: anything passed as
        // an argument is visible to every user on the host via the process list.
        string? envFile = null;
        if (request.Environment.Count > 0)
        {
            envFile = await WriteEnvFileAsync(request.Environment, ct);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in BuildDockerArguments(request, envFile))
            {
                startInfo.ArgumentList.Add(argument);
            }

            logger.LogDebug(
                "docker run {Image} network={Network} timeout={Timeout}",
                request.Image, request.Network, request.Timeout);

            return await ProcessRunner.RunAsync(startInfo, request, ct);
        }
        finally
        {
            if (envFile is not null)
            {
                TryDelete(envFile);
            }
        }
    }

    private static IEnumerable<string> BuildDockerArguments(CommandRequest request, string? envFile)
    {
        yield return "run";
        yield return "--rm";

        // Containment.
        yield return "--network";
        yield return request.Network is NetworkPolicy.None ? "none" : "bridge";

        yield return "--cap-drop";
        yield return "ALL";

        yield return "--security-opt";
        yield return "no-new-privileges";

        // Root filesystem is read-only; only the mounted workspace and a small tmpfs are writable.
        yield return "--read-only";
        yield return "--tmpfs";
        yield return "/tmp:rw,noexec,nosuid,size=512m";

        // Fork-bomb ceiling.
        yield return "--pids-limit";
        yield return "512";

        yield return "--memory";
        yield return request.MemoryLimitBytes.ToString(CultureInfo.InvariantCulture);

        // Disabling swap keeps the memory ceiling meaningful.
        yield return "--memory-swap";
        yield return request.MemoryLimitBytes.ToString(CultureInfo.InvariantCulture);

        yield return "--cpus";
        yield return request.CpuLimit.ToString("0.0", CultureInfo.InvariantCulture);

        // Never run as root inside the container, even though the workspace is bind-mounted.
        yield return "--user";
        yield return "1000:1000";

        yield return "--workdir";
        yield return string.IsNullOrEmpty(request.WorkingSubdirectory)
            ? "/workspace"
            : $"/workspace/{request.WorkingSubdirectory.Replace('\\', '/').Trim('/')}";

        yield return "--volume";
        yield return $"{request.WorkingDirectory}:/workspace";

        if (envFile is not null)
        {
            yield return "--env-file";
            yield return envFile;
        }

        // Override the image's own entrypoint rather than inheriting it: several toolchain
        // images ship an entrypoint that would swallow our arguments (the Kiota image runs
        // `dotnet kiota.dll`, node's runs a shell wrapper). Being explicit means the command
        // that runs is exactly the one we asked for.
        yield return "--entrypoint";
        yield return request.Executable;

        yield return request.Image!;

        foreach (var argument in request.Arguments)
        {
            yield return argument;
        }
    }

    private static async Task<string> WriteEnvFileAsync(
        IReadOnlyDictionary<string, string> environment,
        CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), $"spacecraft-env-{Guid.CreateVersion7():N}");

        // Create with owner-only permissions *before* writing, so the secret is never
        // briefly world-readable. On Windows the per-user temp directory ACL already
        // provides this, and UnixCreateMode is not supported there.
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        var handle = File.Open(path, options);

        await using (handle)
        await using (var writer = new StreamWriter(handle))
        {
            foreach (var (key, value) in environment)
            {
                // docker --env-file takes KEY=VALUE lines and does not process quotes.
                await writer.WriteLineAsync($"{key}={value.ReplaceLineEndings(string.Empty)}".AsMemory(), ct);
            }
        }

        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort; the file holds a short-lived token and lives in the temp dir.
        }
    }
}
