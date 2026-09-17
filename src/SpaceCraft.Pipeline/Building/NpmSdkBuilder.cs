using System.Text;
using System.Text.RegularExpressions;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Pipeline.Sandbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Pipeline.Building;

/// <summary>
/// Installs dependencies, compiles TypeScript, and produces an npm tarball.
/// </summary>
public sealed partial class NpmSdkBuilder(
    ICommandRunner runner,
    IOptions<PipelineOptions> options,
    ILogger<NpmSdkBuilder> logger) : ISdkBuilder
{
    private readonly PipelineOptions _options = options.Value;

    public SdkLanguage Language => SdkLanguage.TypeScript;

    public async Task<BuildOutcome> BuildAsync(BuildRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var log = new StringBuilder();
        var deadline = PipelineOptions.BuildTimeout;

        // Step 1 — dependency install. This is the only part of the build that needs egress.
        //
        // --ignore-scripts is the load-bearing flag: without it, every transitive dependency's
        // preinstall/postinstall hook runs arbitrary code inside our infrastructure. Kiota's
        // runtime packages are pure TypeScript and need no lifecycle scripts, so refusing to
        // run any is free.
        var install = await runner.RunAsync(new CommandRequest
        {
            Executable = "npm",
            Arguments =
            [
                "install",
                "--ignore-scripts",
                "--no-audit",
                "--no-fund",
                "--loglevel", "warn",
                "--registry", _options.DependencyRegistry,
            ],
            WorkingDirectory = request.WorkspaceRoot,
            WorkingSubdirectory = request.SourceDirectory,
            Network = NetworkPolicy.Registry,
            Timeout = deadline,
            Environment = NpmEnvironment(),
        }, ct);

        log.AppendLine("$ npm install --ignore-scripts").AppendLine(install.Output);

        if (!install.Succeeded)
        {
            return BuildOutcome.Failed(
                install.TimedOut
                    ? $"Dependency install timed out after {deadline.TotalMinutes:0.#} minutes."
                    : $"npm install failed with exit code {install.ExitCode}. {install.Tail(10)}",
                log.ToString());
        }

        // Step 2 — compile. No network: everything needed is already on disk.
        var compile = await runner.RunAsync(new CommandRequest
        {
            Executable = "npm",
            Arguments = ["run", "build", "--ignore-scripts"],
            WorkingDirectory = request.WorkspaceRoot,
            WorkingSubdirectory = request.SourceDirectory,
            Network = NetworkPolicy.None,
            Timeout = deadline,
            Environment = NpmEnvironment(),
        }, ct);

        log.AppendLine("$ npm run build").AppendLine(compile.Output);

        if (!compile.Succeeded)
        {
            return BuildOutcome.Failed(
                compile.TimedOut
                    ? $"Compilation timed out after {deadline.TotalMinutes:0.#} minutes."
                    : $"TypeScript compilation failed with exit code {compile.ExitCode}. {compile.Tail(15)}",
                log.ToString());
        }

        // Step 3 — package. Produces the exact tarball that will later be published, so
        // publish never rebuilds and can never emit something the build did not verify.
        var pack = await runner.RunAsync(new CommandRequest
        {
            Executable = "npm",
            Arguments =
            [
                "pack",
                "--ignore-scripts",
                "--pack-destination", $"../{request.ArtifactDirectory}",
                "--loglevel", "warn",
            ],
            WorkingDirectory = request.WorkspaceRoot,
            WorkingSubdirectory = request.SourceDirectory,
            Network = NetworkPolicy.None,
            Timeout = TimeSpan.FromMinutes(2),
            Environment = NpmEnvironment(),
        }, ct);

        log.AppendLine("$ npm pack").AppendLine(pack.Output);

        if (!pack.Succeeded)
        {
            return BuildOutcome.Failed(
                $"npm pack failed with exit code {pack.ExitCode}. {pack.Tail(10)}",
                log.ToString());
        }

        var fileName = ExtractTarballName(pack.Output) ?? ConventionalTarballName(request.PackageName, request.Version);
        logger.LogInformation("Packed {PackageName}@{Version} as {FileName}", request.PackageName, request.Version, fileName);

        return new BuildOutcome
        {
            Succeeded = true,
            Output = log.ToString(),
            ArtifactFileName = fileName,
            // npm pack writes a gzipped tarball. Said here because the builder is the only
            // thing that knows — a .nupkg or a .jar reaching the same field is a zip.
            ArtifactMediaType = "application/gzip",
        };
    }

    /// <summary>
    /// Build steps run with a deliberately bare environment: no registry token is present
    /// at any point, so a lifecycle script that slipped through has nothing to steal.
    /// Caches and npm's global prefix are redirected to the writable tmpfs, because the
    /// container's root filesystem is mounted read-only.
    /// </summary>
    private static Dictionary<string, string> NpmEnvironment() => new(StringComparer.Ordinal)
    {
        ["npm_config_cache"] = "/tmp/npm-cache",
        ["npm_config_update_notifier"] = "false",
        ["npm_config_prefix"] = "/tmp/npm-global",
        ["HOME"] = "/tmp",
        ["CI"] = "true",
    };

    /// <summary>npm prints the tarball name on the last line of a successful pack.</summary>
    internal static string? ExtractTarballName(string output)
    {
        var match = TarballName().Matches(output).LastOrDefault();
        return match?.Value;
    }

    /// <summary>Mirrors npm's own naming: "@acme/billing" at 1.2.3 becomes acme-billing-1.2.3.tgz.</summary>
    internal static string ConventionalTarballName(string packageName, string version) =>
        $"{packageName.TrimStart('@').Replace('/', '-')}-{version}.tgz";

    [GeneratedRegex(@"[A-Za-z0-9][A-Za-z0-9._-]*\.tgz")]
    private static partial Regex TarballName();
}
