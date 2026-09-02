using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpaceCraft.Domain.Projects;
using Microsoft.Extensions.Logging;

namespace SpaceCraft.Pipeline.Generation;

public sealed record KiotaDependency(string Name, string Version, string Type);

/// <summary>
/// Supplies the runtime packages a Kiota-generated client needs.
/// <para>
/// Reading them from the Kiota package rather than hard-coding them means the generated
/// package.json always matches the generator that produced the sources. Pinning them by hand
/// guarantees a silent drift the first time Kiota is upgraded.
/// </para>
/// <para>
/// This used to shell out to <c>kiota info --json</c>, which made the versions depend on
/// whichever CLI happened to be on PATH. They now come from the CLI package's own
/// appsettings.json, embedded at build time from the version in Directory.Packages.props —
/// see SpaceCraft.Pipeline.csproj. Same data, same origin, no process and nothing to install.
/// </para>
/// </summary>
public interface IKiotaDependencyProvider
{
    /// <summary>Resolves the runtime packages for a language.</summary>
    IReadOnlyList<KiotaDependency> GetRuntimeDependencies(SdkLanguage language);
}

public sealed class KiotaDependencyProvider(
    ILogger<KiotaDependencyProvider> logger) : IKiotaDependencyProvider
{
    /// <summary>
    /// Only these categories belong in a generated SDK. "Authentication" is an Azure-specific
    /// provider our consumers do not need, and "Bundle" is a convenience meta-package that
    /// would pull the whole set in transitively.
    /// </summary>
    private static readonly string[] RequiredTypes = ["Abstractions", "Http", "Serialization"];

    /// <summary>Embedded by the EmbedKiotaLanguages target; see SpaceCraft.Pipeline.csproj.</summary>
    private const string LanguagesResource = "kiota-languages.json";

    private readonly ConcurrentDictionary<SdkLanguage, IReadOnlyList<KiotaDependency>> _cache = new();

    public IReadOnlyList<KiotaDependency> GetRuntimeDependencies(SdkLanguage language) =>
        _cache.GetOrAdd(language, static (lang, log) => Parse(ReadLanguageSection(lang), log), logger);

    /// <summary>
    /// Pulls one language out of the embedded Kiota settings. The shape is the CLI's, because
    /// the file is the CLI's: <c>Languages.TypeScript.Dependencies[]</c> of Name/Version/Type.
    /// </summary>
    private static string ReadLanguageSection(SdkLanguage language)
    {
        using var stream = typeof(KiotaDependencyProvider).Assembly
            .GetManifestResourceStream(LanguagesResource)
            ?? throw new InvalidOperationException(
                $"{LanguagesResource} is not embedded. The build target that takes it from the "
                + "Kiota CLI package did not run — see SpaceCraft.Pipeline.csproj.");

        using var document = JsonDocument.Parse(stream);
        var name = language.SettingsKey();

        if (!document.RootElement.TryGetProperty("Languages", out var languages)
            || !languages.TryGetProperty(name, out var section))
        {
            throw new InvalidOperationException(
                $"Kiota's settings describe no language named '{name}'.");
        }

        return section.GetRawText();
    }

    internal static IReadOnlyList<KiotaDependency> Parse(string json, ILogger logger)
    {
        var payload = JsonSerializer.Deserialize<KiotaInfoPayload>(json, JsonOptions)
            ?? throw new InvalidOperationException("Kiota's language information was unreadable.");

        var dependencies = payload.Dependencies
            .Where(d => RequiredTypes.Contains(d.Type, StringComparer.OrdinalIgnoreCase))
            .Select(d => new KiotaDependency(d.Name, d.Version, d.Type))
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

        if (dependencies.Count == 0)
        {
            throw new InvalidOperationException(
                $"Kiota declares no usable runtime dependencies for this language. Expected at "
                + $"least one of: {string.Join(", ", RequiredTypes)}.");
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
