using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Pipeline.Sandbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Pipeline.Generation;

public sealed record KiotaDependency(string Name, string Version, string Type);

/// <summary>
/// Supplies the runtime packages a Kiota-generated client needs.
/// <para>
/// Asking the CLI (<c>kiota info --json</c>) rather than hard-coding versions means the
/// generated package.json always matches the generator that produced the sources. Pinning
/// them by hand guarantees a silent drift the first time Kiota is upgraded.
/// </para>
/// </summary>
public interface IKiotaDependencyProvider
{
    /// <summary>
    /// Resolves the runtime packages for a language. A description must be supplied because
    /// the CLI's `info` command loads one even when only reporting dependencies.
    /// </summary>
    Task<IReadOnlyList<KiotaDependency>> GetRuntimeDependenciesAsync(
        SdkLanguage language,
        string workspaceRoot,
        string specRelativePath,
        CancellationToken ct = default);
}

public sealed class KiotaDependencyProvider(
    ICommandRunner runner,
    IOptions<PipelineOptions> options,
    ILogger<KiotaDependencyProvider> logger) : IKiotaDependencyProvider
{
    /// <summary>
    /// Only these categories belong in a generated SDK. "Authentication" is an Azure-specific
    /// provider our consumers do not need, and "Bundle" is a convenience meta-package that
    /// would pull the whole set in transitively.
    /// </summary>
    private static readonly string[] RequiredTypes = ["Abstractions", "Http", "Serialization"];

    private readonly PipelineOptions _options = options.Value;
    private readonly ConcurrentDictionary<SdkLanguage, IReadOnlyList<KiotaDependency>> _cache = new();

    public async Task<IReadOnlyList<KiotaDependency>> GetRuntimeDependenciesAsync(
        SdkLanguage language,
        string workspaceRoot,
        string specRelativePath,
        CancellationToken ct = default)
    {
        if (_cache.TryGetValue(language, out var cached))
        {
            return cached;
        }

        var (executable, prefix) = KiotaSdkGenerator.KiotaInvocation(_options.Sandbox);

        var result = await runner.RunAsync(new CommandRequest
        {
            Executable = executable,
            Arguments = [.. prefix, "info", "--openapi", specRelativePath, "--language", language.KiotaName(), "--json"],
            WorkingDirectory = workspaceRoot,
            Image = _options.KiotaImage,
            Network = NetworkPolicy.None,
            Timeout = TimeSpan.FromMinutes(2),
            Environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["KIOTA_OFFLINE_ENABLED"] = "true",
            },
        }, ct);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not read Kiota dependencies for {language}: exit {result.ExitCode}. {result.Tail(5)}");
        }

        var dependencies = Parse(result.Output, logger);
        _cache[language] = dependencies;
        return dependencies;
    }

    internal static IReadOnlyList<KiotaDependency> Parse(string cliOutput, ILogger logger)
    {
        // The CLI prefixes JSON with an update-check banner when a newer version exists,
        // so seek to the first object rather than parsing the whole stream.
        var start = cliOutput.IndexOf('{');
        if (start < 0)
        {
            throw new InvalidOperationException("kiota info produced no JSON payload.");
        }

        var payload = JsonSerializer.Deserialize<KiotaInfoPayload>(cliOutput[start..], JsonOptions)
            ?? throw new InvalidOperationException("kiota info produced an unreadable JSON payload.");

        var dependencies = payload.Dependencies
            .Where(d => RequiredTypes.Contains(d.Type, StringComparer.OrdinalIgnoreCase))
            .Select(d => new KiotaDependency(d.Name, d.Version, d.Type))
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

        if (dependencies.Count == 0)
        {
            throw new InvalidOperationException("kiota info returned no usable runtime dependencies.");
        }

        // Kiota's TypeScript packages are still pre-release. Worth a log line so nobody is
        // surprised that published SDKs depend on preview packages.
        foreach (var preview in dependencies.Where(d => d.Version.Contains('-', StringComparison.Ordinal)))
        {
            logger.LogInformation("Kiota runtime dependency {Name} is pre-release ({Version})", preview.Name, preview.Version);
        }

        return dependencies;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record KiotaInfoPayload
    {
        [JsonPropertyName("dependencies")]
        public List<KiotaInfoDependency> Dependencies { get; init; } = [];
    }

    private sealed record KiotaInfoDependency
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;
    }
}
