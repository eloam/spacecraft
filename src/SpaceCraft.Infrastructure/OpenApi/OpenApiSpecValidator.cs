using SpaceCraft.Domain.Projects;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace SpaceCraft.Infrastructure.OpenApi;

/// <summary>
/// Parses OpenAPI documents with Microsoft.OpenApi, then applies SpaceCraft's own policy checks.
/// </summary>
public sealed class OpenApiSpecValidator(ILogger<OpenApiSpecValidator> logger) : ISpecValidator
{
    public async Task<SpecValidationResult> ValidateAsync(
        Stream content,
        string fileName,
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
            diagnostics.Add(new SpecDiagnostic(DiagnosticSeverity.Warning, warning.Message, warning.Pointer));
        }

        var document = result.Document;
        if (document is null)
        {
            diagnostics.Add(new SpecDiagnostic(DiagnosticSeverity.Error, "The document could not be read as OpenAPI."));
            return new SpecValidationResult(SpecValidationStatus.Invalid, null, null, diagnostics, 0);
        }

        var operationCount = CountOperations(document);
        diagnostics.AddRange(ApplyPolicy(document, operationCount));

        var hasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        var status = hasErrors
            ? SpecValidationStatus.Invalid
            : diagnostics.Count > 0
                ? SpecValidationStatus.ValidWithWarnings
                : SpecValidationStatus.Valid;

        return new SpecValidationResult(
            status,
            document.Info?.Title,
            document.Info?.Version,
            diagnostics,
            operationCount);
    }

    /// <summary>
    /// Checks that go beyond "is this valid OpenAPI" and into "can we emit a usable SDK from it".
    /// </summary>
    private static IEnumerable<SpecDiagnostic> ApplyPolicy(OpenApiDocument document, int operationCount)
    {
        if (string.IsNullOrWhiteSpace(document.Info?.Title))
        {
            yield return new SpecDiagnostic(DiagnosticSeverity.Error, "info.title is required to name the generated package.", "#/info/title");
        }

        if (string.IsNullOrWhiteSpace(document.Info?.Version))
        {
            yield return new SpecDiagnostic(DiagnosticSeverity.Error, "info.version is required; it becomes the SDK version.", "#/info/version");
        }

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

        // Missing operationIds are survivable — generators synthesise names from the path —
        // but the resulting method names are poor, so it is worth telling the operator.
        foreach (var (path, item) in document.Paths ?? [])
        {
            foreach (var (method, operation) in item.Operations ?? [])
            {
                if (string.IsNullOrWhiteSpace(operation.OperationId))
                {
                    yield return new SpecDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"{method.ToString().ToUpperInvariant()} {path} has no operationId; the generated method name will be derived from the path.",
                        $"#/paths/{path}");
                }
            }
        }
    }

    private static int CountOperations(OpenApiDocument document) =>
        document.Paths?.Sum(p => p.Value.Operations?.Count ?? 0) ?? 0;

    /// <summary>
    /// Picks the reader by extension. JSON is a subset of YAML, so the YAML reader would
    /// handle both — but the JSON reader gives better diagnostics on .json files.
    /// </summary>
    private static string DetectFormat(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".json" ? "json" : "yaml";
}
