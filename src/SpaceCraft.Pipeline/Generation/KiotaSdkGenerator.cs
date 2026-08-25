using System.Text.RegularExpressions;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Pipeline.Execution;
using SpaceCraft.Pipeline.Sandbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Pipeline.Generation;

/// <summary>
/// Emits SDK sources by shelling out to the Kiota CLI.
/// <para>
/// Kiota is used rather than a hand-written emitter because it already covers every
/// language on SpaceCraft's roadmap (C#, Java, TypeScript, Python, PHP, Go) and is maintained
/// by Microsoft. The trade-off is that generated clients depend on Kiota's per-language
/// abstraction packages and follow its request-builder style rather than reading like a
/// hand-rolled SDK.
/// </para>
/// </summary>
public sealed partial class KiotaSdkGenerator(
    ICommandRunner runner,
    IOptions<PipelineOptions> options,
    ILogger<KiotaSdkGenerator> logger) : ISdkGenerator
{
    private readonly PipelineOptions _options = options.Value;

    public SdkLanguage Language => SdkLanguage.TypeScript;

    public async Task<GenerationOutcome> GenerateAsync(GenerationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var clientName = request.ClientClassName ?? DeriveClientName(request.ApiTitle, request.PackageName);

        var (executable, prefix) = KiotaInvocation(_options.Sandbox);

        var command = new CommandRequest
        {
            Executable = executable,
            Arguments =
            [
                .. prefix,
                "generate",
                "--openapi", request.SpecFilePath,
                "--language", request.Language.KiotaName(),
                "--output", request.OutputDirectory,
                "--class-name", clientName,
                "--namespace-name", NamespaceFor(request.PackageName),
                "--clean-output",
                "--log-level", "Warning",
            ],
            WorkingDirectory = request.WorkspaceRoot,
            Image = _options.KiotaImage,
            // Generation reads a local file and writes local files. It needs no egress —
            // external $refs were already rejected at validation time.
            Network = NetworkPolicy.None,
            Timeout = _options.GenerateTimeout,
            // Kiota checks for updates and emits telemetry by default. The sandbox has no
            // egress, so both would just stall the step until they time out.
            Environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["KIOTA_TUTORIAL_ENABLED"] = "false",
                ["KIOTA_OFFLINE_ENABLED"] = "true",
            },
        };

        var result = await runner.RunAsync(command, ct);

        if (!result.Succeeded)
        {
            logger.LogWarning("Kiota generation failed with exit code {ExitCode}", result.ExitCode);
            return GenerationOutcome.Failed(
                result.TimedOut
                    ? $"Generation timed out after {_options.GenerateTimeout.TotalMinutes:0.#} minutes."
                    : $"Kiota exited with code {result.ExitCode}. {result.Tail(10)}",
                result.Output);
        }

        return new GenerationOutcome
        {
            Succeeded = true,
            Output = result.Output,
        };
    }

    /// <summary>
    /// How to invoke the Kiota CLI.
    /// <para>
    /// The official container image is distroless and ships no <c>kiota</c> executable — it
    /// is a .NET assembly launched as <c>dotnet kiota.dll</c> from /app. On a developer
    /// machine with sandboxing disabled, the globally installed CLI is a plain <c>kiota</c>
    /// binary instead.
    /// </para>
    /// </summary>
    internal static (string Executable, string[] Prefix) KiotaInvocation(SandboxMode sandbox) =>
        sandbox.UsesContainers() ? ("dotnet", ["/app/kiota.dll"]) : ("kiota", []);

    /// <summary>
    /// Turns "Keycloak Admin REST API" into "KeycloakAdminRestApiClient" — a valid
    /// identifier in every target language.
    /// </summary>
    internal static string DeriveClientName(string? apiTitle, string packageName)
    {
        var source = !string.IsNullOrWhiteSpace(apiTitle) ? apiTitle : packageName;

        // Drop an npm scope before deriving an identifier from a package name.
        source = source.Contains('/') ? source[(source.LastIndexOf('/') + 1)..] : source;

        var words = NonWord().Split(source).Where(w => w.Length > 0).ToArray();
        if (words.Length == 0)
        {
            return "ApiClient";
        }

        var name = string.Concat(words.Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

        if (char.IsDigit(name[0]))
        {
            name = "Api" + name;
        }

        return name.EndsWith("Client", StringComparison.OrdinalIgnoreCase) ? name : name + "Client";
    }

    /// <summary>
    /// "@acme/billing-sdk" becomes "acme.billing_sdk". Hyphens map to underscores rather
    /// than being dropped: no target language accepts a hyphen in an identifier, but
    /// deleting it silently welds words together ("billingsdk").
    /// </summary>
    internal static string NamespaceFor(string packageName)
    {
        var name = packageName.TrimStart('@').Replace('/', '.').Replace('-', '_');
        return NonNamespace().Replace(name, string.Empty).Trim('.') is { Length: > 0 } cleaned
            ? cleaned
            : "sdk";
    }

    [GeneratedRegex(@"[^A-Za-z0-9]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"[^A-Za-z0-9._]")]
    private static partial Regex NonNamespace();
}
