using SpaceCraft.Domain.Projects;
using SpaceCraft.Pipeline.Sandbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Pipeline.Publishing;

/// <summary>
/// Publishes a tarball to npm.
/// <para>
/// This is the only step in the pipeline that holds a credential, and it holds it for one
/// command. The token is put in the child process's environment and referenced from a
/// generated <c>.npmrc</c> — never on the command line, which is world-readable through the
/// process list — and the file is deleted in a <c>finally</c>. It is registered for redaction
/// so it cannot survive into a stored build log.
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
            $"{AuthKeyFor(request.RegistryUrl)}=${{SPACECRAFT_NPM_TOKEN}}\nregistry={request.RegistryUrl}\n",
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
                Timeout = PipelineOptions.PublishTimeout,
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
                    RegistryUrl = BuildRegistryUrl(request.RegistryUrl, request.PackageName, request.Version),
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
                    RegistryUrl = BuildRegistryUrl(request.RegistryUrl, request.PackageName, request.Version),
                };
            }

            return PublishOutcome.Failed(
                result.TimedOut
                    ? $"Publish timed out after {PipelineOptions.PublishTimeout.TotalMinutes:0.#} minutes."
                    : $"npm publish failed with exit code {result.ExitCode}. {result.Tail(10)}",
                result.Output);
        }
        finally
        {
            TryDelete(npmrcPath);
        }
    }

    /// <summary>
    /// A version that is already on the registry, which is a no-op rather than a failure:
    /// registries are append-only, so re-running a pipeline must not report an error.
    ///
    /// <para>
    /// Matched on the message because there is no status code to rely on — npmjs answers 403
    /// where Verdaccio answers 409, and each phrases it its own way. Now that the destination
    /// comes from the credential, every registry an organization uses ends up here, so the
    /// list is deliberately broad: mistaking this for a failure only makes a harmless re-run
    /// look broken.
    /// </para>
    /// </summary>
    internal static bool IsVersionConflict(string output) =>
        output.Contains("cannot publish over the previously published version", StringComparison.OrdinalIgnoreCase)
        || output.Contains("You cannot publish over the previously published versions", StringComparison.OrdinalIgnoreCase)
        || output.Contains("EPUBLISHCONFLICT", StringComparison.OrdinalIgnoreCase)
        // Verdaccio, and the registries that copy its wording.
        || output.Contains("this package is already present", StringComparison.OrdinalIgnoreCase)
        // Nexus and Artifactory both use this phrasing for a redeploy of a released version.
        || output.Contains("cannot be republished", StringComparison.OrdinalIgnoreCase)
        || output.Contains("version already exists", StringComparison.OrdinalIgnoreCase);

    /// <summary>Turns a registry URL into the .npmrc auth key npm expects.</summary>
    internal static string AuthKeyFor(string registryUrl)
    {
        var uri = new Uri(registryUrl);
        var path = uri.AbsolutePath.TrimEnd('/');
        return $"//{uri.Authority}{path}/:_authToken";
    }

    /// <summary>
    /// Where a human goes to look at what was just published. npmjs serves its packages from
    /// a different host than its API; every other registry serves them from its own.
    /// </summary>
    private static string BuildRegistryUrl(string registryUrl, string packageName, string version) =>
        registryUrl.Contains("registry.npmjs.org", StringComparison.OrdinalIgnoreCase)
            ? $"https://www.npmjs.com/package/{packageName}/v/{version}"
            : $"{registryUrl.TrimEnd('/')}/{packageName}";

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
