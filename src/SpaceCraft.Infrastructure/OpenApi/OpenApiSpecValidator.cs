using System.Text.RegularExpressions;
using SpaceCraft.Domain.Projects;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

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
    private static IEnumerable<SpecDiagnostic> ApplyPolicy(OpenApiDocument document, int operationCount)
    {
        // info.title and info.version are not checked here: the parser's own InfoRequiredFields
        // rule already reports both, and saying it twice would put two findings on one line
        // for a single problem.

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
