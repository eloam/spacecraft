using System.Text.RegularExpressions;
using SpaceCraft.Domain.Projects;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

using SpaceCraft.Domain.Registries;
namespace SpaceCraft.Infrastructure.OpenApi;

/// <summary>
/// Parses OpenAPI documents with Microsoft.OpenApi, then applies SpaceCraft's own policy checks.
/// </summary>
public sealed partial class OpenApiSpecValidator(ILogger<OpenApiSpecValidator> logger) : ISpecValidator
{
    public async Task<SpecValidationResult> ValidateAsync(
        Stream content,
        string fileName,
        bool strict = false,
        string? previousApiVersion = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.CanSeek && content.Length > SpecLimits.MaxSizeInBytes)
        {
            return SpecValidationResult.Invalid(new SpecDiagnostic(
                DiagnosticSeverity.Error,
                $"Document is {content.Length / 1024 / 1024} MB; the limit is {SpecLimits.MaxSizeInBytes / 1024 / 1024} MB."));
        }

        var settings = new OpenApiReaderSettings
        {
            // The single most important line in this file. With external refs enabled, a
            // spec containing $ref: http://169.254.169.254/... would make our worker fetch
            // cloud instance metadata on the uploader's behalf. Everything must be self-contained.
            LoadExternalRefs = false,
            LeaveStreamOpen = true,
        };
        settings.AddYamlReader();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SpecLimits.ParseTimeout);

        ReadResult result;
        try
        {
            result = await OpenApiDocument.LoadAsync(content, DetectFormat(fileName), settings, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Cancelled by our own timeout, not by the caller: the document is pathological.
            logger.LogWarning("Parsing {FileName} exceeded {Timeout}", fileName, SpecLimits.ParseTimeout);
            return SpecValidationResult.Invalid(new SpecDiagnostic(
                DiagnosticSeverity.Error,
                $"Parsing timed out after {SpecLimits.ParseTimeout.TotalSeconds:0}s. The document may contain deeply nested or recursive structures."));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse {FileName}", fileName);
            return SpecValidationResult.Invalid(new SpecDiagnostic(
                DiagnosticSeverity.Error,
                $"Could not parse the document: {ex.Message}"));
        }

        var diagnostics = new List<SpecDiagnostic>();

        foreach (var error in result.Diagnostic?.Errors ?? [])
        {
            diagnostics.Add(new SpecDiagnostic(DiagnosticSeverity.Error, error.Message, error.Pointer));
        }

        foreach (var warning in result.Diagnostic?.Warnings ?? [])
        {
            // Under strict, a document has to parse without remarks at all. The parser only
            // warns about things like a $ref pointing at a schema that is not there — which
            // stops generation dead, whatever the severity attached to it.
            diagnostics.Add(new SpecDiagnostic(
                strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                warning.Message,
                warning.Pointer));
        }

        var document = result.Document;
        if (document is null)
        {
            diagnostics.Add(new SpecDiagnostic(DiagnosticSeverity.Error, "The document could not be read as OpenAPI."));
            return new SpecValidationResult(SpecValidationStatus.Invalid, null, null, null, diagnostics, 0);
        }

        var operationCount = CountOperations(document);
        diagnostics.AddRange(ApplyPolicy(document, operationCount));

        if (strict)
        {
            diagnostics.AddRange(StrictSdkPolicy.Apply(document));
            diagnostics.AddRange(StrictSdkPolicy.AgainstPrevious(document, previousApiVersion));
        }

        // Keyed on severity rather than on "are there any findings": an information-only
        // document is valid, and calling it ValidWithWarnings would put a warning badge on a
        // spec that has none.
        var status = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
            ? SpecValidationStatus.Invalid
            : diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning)
                ? SpecValidationStatus.ValidWithWarnings
                : SpecValidationStatus.Valid;

        return new SpecValidationResult(
            status,
            document.Info?.Title,
            document.Info?.Version,
            DeclaredVersion(content, result.Diagnostic?.SpecificationVersion),
            diagnostics,
            operationCount);
    }

    /// <summary>
    /// Checks that go beyond "is this valid OpenAPI" and into "can we emit a usable SDK from it".
    /// </summary>
    /// <summary>
    /// Reports a version some registries will refuse, naming which.
    /// </summary>
    /// <remarks>
    /// A warning rather than an error, and deliberately so: which registry this document ends
    /// up publishing to is not known here — one spec feeds every target in its project, and
    /// they may publish to different registries. <c>1.2.3.4</c> is an ordinary NuGet version
    /// and inexpressible in npm, so calling it invalid would repeat the mistake this replaces:
    /// one registry's rules applied to all of them.
    /// </remarks>
    private static IEnumerable<SpecDiagnostic> VersionFormat(OpenApiDocument document)
    {
        var declared = document.Info?.Version;
        if (string.IsNullOrWhiteSpace(declared))
        {
            yield break;
        }

        var accepted = new List<RegistryKind>();
        var refused = new List<RegistryKind>();

        foreach (var registry in Enum.GetValues<RegistryKind>())
        {
            if (RegistryVersion.Render(registry, declared).Accepted)
            {
                accepted.Add(registry);
            }
            else
            {
                refused.Add(registry);
            }
        }

        if (refused.Count == 0)
        {
            // Accepted everywhere, but not necessarily under the string that was written:
            // npm completes 1.0 to 1.0.0 where Maven keeps it as it stands. Said at upload,
            // which is where the person who wrote the version is looking.
            var rewritten = accepted
                .Select(registry => (Registry: registry, RegistryVersion.Render(registry, declared).Version))
                .Where(r => !string.Equals(r.Version, declared, StringComparison.Ordinal))
                .ToList();

            if (rewritten.Count > 0)
            {
                yield return new SpecDiagnostic(
                    DiagnosticSeverity.Information,
                    $"info.version '{declared}' is published as "
                    + string.Join(", ", rewritten.Select(r => $"'{r.Version}' to {r.Registry}"))
                    + ", and as declared elsewhere.",
                    "#/info/version");
            }

            yield break;
        }

        // Refused everywhere is a different statement from refused by some, and the second is
        // the one that needs the list.
        yield return accepted.Count == 0
            ? new SpecDiagnostic(
                DiagnosticSeverity.Error,
                $"info.version '{declared}' cannot be published to any registry. "
                + "Set a version like 1.0.0.",
                "#/info/version")
            : new SpecDiagnostic(
                DiagnosticSeverity.Warning,
                $"info.version '{declared}' is not a three-part version. "
                + $"{string.Join(", ", accepted)} accept it; {string.Join(", ", refused)} refuse it, "
                + "so targets publishing there will fail. Set a version like 1.0.0 to publish anywhere.",
                "#/info/version");
    }

    private static IEnumerable<SpecDiagnostic> ApplyPolicy(OpenApiDocument document, int operationCount)
    {
        // info.title is not checked here: the parser's own InfoRequiredFields rule already
        // reports it, and saying it twice would put two findings on one line for a single
        // problem. That rule reports whether info.version is *present*, which is not the same
        // as whether a registry will take it — nothing checked the format at all, in strict
        // mode or out of it, and an unusable version was discovered by a run failing.
        foreach (var finding in VersionFormat(document)) yield return finding;

        if (operationCount == 0)
        {
            yield return new SpecDiagnostic(DiagnosticSeverity.Error, "The document declares no operations, so there is nothing to generate.", "#/paths");
        }

        if (operationCount > SpecLimits.MaxOperations)
        {
            yield return new SpecDiagnostic(
                DiagnosticSeverity.Error,
                $"The document declares {operationCount} operations; the limit is {SpecLimits.MaxOperations}.",
                "#/paths");
        }

        var schemaCount = document.Components?.Schemas?.Count ?? 0;
        if (schemaCount > SpecLimits.MaxSchemas)
        {
            yield return new SpecDiagnostic(
                DiagnosticSeverity.Error,
                $"The document declares {schemaCount} schemas; the limit is {SpecLimits.MaxSchemas}.",
                "#/components/schemas");
        }

        // No operationId rule here. Generated against Kiota 1.34.1, a document with
        // operationIds and the same document without them produce byte-identical output:
        // the client is built from paths and verbs, and the operationId is never read. The
        // note this used to emit told operators to fix something that changes nothing.
    }

    /// <summary>
    /// Builds an RFC 6901 pointer. The escaping is not decoration: path keys contain "/",
    /// so an unescaped "#/paths//admin/realms" cannot be told apart from a nesting of empty
    /// and "admin" — which is exactly what the viewer has to resolve to find a line.
    /// </summary>
    private static string Pointer(params string[] segments) =>
        "#/" + string.Join('/', segments.Select(s => s.Replace("~", "~0").Replace("/", "~1")));

    private static int CountOperations(OpenApiDocument document) =>
        document.Paths?.Sum(p => p.Value.Operations?.Count ?? 0) ?? 0;

    /// <summary>
    /// The exact version string the document declares, e.g. "3.0.3".
    ///
    /// The parser reports only the family it read the document as — 3.0 or 3.1 — so the patch
    /// digit has to come from the text. The declared string is only trusted when it agrees
    /// with what the parser decided: that rules out a stray "openapi:" appearing inside a
    /// description before the real field, without having to parse the document twice.
    /// </summary>
    private static string? DeclaredVersion(Stream content, OpenApiSpecVersion? parsed)
    {
        var family = parsed switch
        {
            OpenApiSpecVersion.OpenApi2_0 => "2.0",
            OpenApiSpecVersion.OpenApi3_0 => "3.0",
            OpenApiSpecVersion.OpenApi3_1 => "3.1",
            _ => null,
        };

        if (family is null || !content.CanSeek) return family;

        try
        {
            content.Position = 0;
            using var reader = new StreamReader(content, leaveOpen: true);

            // The version is declared at the top level, so it is within reach of the first
            // handful of lines in every document that has one — no need to scan 10 MB.
            for (var line = 0; line < HeadLinesScanned; line++)
            {
                if (reader.ReadLine() is not { } text) break;

                var match = DeclaredVersionPattern().Match(text);
                if (match.Success && match.Groups[1].Value.StartsWith(family, StringComparison.Ordinal))
                {
                    return match.Groups[1].Value;
                }
            }
        }
        catch (IOException)
        {
            // Reading the text is a nicety; the family is already known.
        }

        return family;
    }

    private const int HeadLinesScanned = 64;

    [GeneratedRegex("""^\s*"?(?:openapi|swagger)"?\s*:\s*"?([0-9]+\.[0-9]+(?:\.[0-9]+)?)"?""")]
    private static partial Regex DeclaredVersionPattern();

    /// <summary>
    /// Picks the reader by extension. JSON is a subset of YAML, so the YAML reader would
    /// handle both — but the JSON reader gives better diagnostics on .json files.
    /// </summary>
    private static string DetectFormat(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".json" ? "json" : "yaml";
}
