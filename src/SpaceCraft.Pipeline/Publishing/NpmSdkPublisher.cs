using SpaceCraft.Domain.Projects;
using SpaceCraft.Pipeline.Sandbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Pipeline.Publishing;

/// <summary>
/// Publishes a tarball to npm.
/// <para>
/// This is the only step in the pipeline that holds a credential, and it holds it for one
/// command. The token reaches the container through an env file (not the command line, which
/// is world-readable via the process list) and is registered for redaction so it cannot
/// survive into a stored build log.
/// </para>
/// </summary>
public sealed class NpmSdkPublisher(
    ICommandRunner runner,
    IOptions<PipelineOptions> options,
    ILogger<NpmSdkPublisher> logger) : ISdkPublisher
{
    private readonly PipelineOptions _options = options.Value;

    public RegistryKind Registry => RegistryKind.Npm;

    public async Task<PublishOutcome> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var artifactHostDirectory = Path.Combine(request.WorkspaceRoot, request.ArtifactDirectory);
        var tarball = Path.Combine(artifactHostDirectory, request.ArtifactFileName);

        if (!File.Exists(tarball))
        {
            return PublishOutcome.Failed(
                $"Build artifact '{request.ArtifactFileName}' is missing; nothing to publish.",
                string.Empty);
        }

        // npm reads credentials from .npmrc. Referencing an environment variable keeps the
        // literal token out of the file, so a workspace that somehow survives cleanup
        // contains no secret.
        var npmrcPath = Path.Combine(artifactHostDirectory, ".npmrc");
        await File.WriteAllTextAsync(
            npmrcPath,
            $"{AuthKeyFor(_options.NpmRegistry)}=${{SPACECRAFT_NPM_TOKEN}}\nregistry={_options.NpmRegistry}\n",
            ct);

        try
        {
            var result = await runner.RunAsync(new CommandRequest
            {
                Executable = "npm",
                Arguments =
                [
                    "publish",
                    request.ArtifactFileName,
                    "--access", "public",
                    "--ignore-scripts",
                    "--loglevel", "warn",
                ],
                WorkingDirectory = request.WorkspaceRoot,
                WorkingSubdirectory = request.ArtifactDirectory,
                Network = NetworkPolicy.Registry,
                Timeout = _options.PublishTimeout,
                Environment = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["SPACECRAFT_NPM_TOKEN"] = request.Token,
                    ["npm_config_userconfig"] = $"/workspace/{request.ArtifactDirectory}/.npmrc",
                    ["npm_config_cache"] = "/tmp/npm-cache",
                    ["npm_config_update_notifier"] = "false",
                    ["HOME"] = "/tmp",
                    ["CI"] = "true",
                },
                SecretsToRedact = [request.Token],
            }, ct);

            if (result.Succeeded)
            {
                logger.LogInformation("Published {PackageName}@{Version}", request.PackageName, request.Version);
                return new PublishOutcome
                {
                    Kind = PublishResultKind.Succeeded,
                    Output = result.Output,
                    RegistryUrl = BuildRegistryUrl(request.PackageName, request.Version),
                };
            }

            if (IsVersionConflict(result.Output))
            {
                logger.LogInformation(
                    "{PackageName}@{Version} already exists on npm; treating as satisfied",
                    request.PackageName, request.Version);

                return new PublishOutcome
                {
                    Kind = PublishResultKind.AlreadyExists,
                    Output = result.Output,
                    RegistryUrl = BuildRegistryUrl(request.PackageName, request.Version),
                };
            }

            return PublishOutcome.Failed(
                result.TimedOut
                    ? $"Publish timed out after {_options.PublishTimeout.TotalMinutes:0.#} minutes."
                    : $"npm publish failed with exit code {result.ExitCode}. {result.Tail(10)}",
                result.Output);
        }
        finally
        {
            TryDelete(npmrcPath);
        }
    }

    /// <summary>
    /// npm reports an existing version as a 403 rather than a 409, so the message is the
    /// only reliable signal. Both the modern and legacy wordings are matched.
    /// </summary>
    internal static bool IsVersionConflict(string output) =>
        output.Contains("cannot publish over the previously published version", StringComparison.OrdinalIgnoreCase)
        || output.Contains("You cannot publish over the previously published versions", StringComparison.OrdinalIgnoreCase)
        || output.Contains("EPUBLISHCONFLICT", StringComparison.OrdinalIgnoreCase);

    /// <summary>Turns a registry URL into the .npmrc auth key npm expects.</summary>
    internal static string AuthKeyFor(string registryUrl)
    {
        var uri = new Uri(registryUrl);
        var path = uri.AbsolutePath.TrimEnd('/');
        return $"//{uri.Authority}{path}/:_authToken";
    }

    private string BuildRegistryUrl(string packageName, string version) =>
        _options.NpmRegistry.Contains("registry.npmjs.org", StringComparison.OrdinalIgnoreCase)
            ? $"https://www.npmjs.com/package/{packageName}/v/{version}"
            : $"{_options.NpmRegistry.TrimEnd('/')}/{packageName}";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The file references an env var rather than the token itself, and the whole
            // workspace is deleted at the end of the run.
        }
    }
}
