using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Infrastructure.OpenApi;

public enum DiagnosticSeverity
{
    /// <summary>
    /// Worth telling the operator, but nothing is wrong: the document is valid and the SDK
    /// it produces is usable. Numbered 0 so the existing Warning and Error values — already
    /// serialised into stored diagnostics — keep meaning what they meant.
    /// </summary>
    Information = 0,

    Warning = 1,
    Error = 2,
}

/// <summary>One parser or policy finding, addressed to the operator who uploaded the spec.</summary>
public sealed record SpecDiagnostic(DiagnosticSeverity Severity, string Message, string? JsonPointer = null);

public sealed record SpecValidationResult(
    SpecValidationStatus Status,
    string? ApiTitle,
    string? ApiVersion,
    /// <summary>
    /// The OpenAPI version the document declares — "3.0.3", not the API's own version.
    /// It decides what a generator has to cope with (3.1 moves to JSON Schema 2020-12), so
    /// it belongs next to the document rather than buried in it.
    /// </summary>
    string? OpenApiVersion,
    IReadOnlyList<SpecDiagnostic> Diagnostics,
    int OperationCount)
{
    public int InformationCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Information);

    public int WarningCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

    public int ErrorCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);

    public bool IsGeneratable => Status is SpecValidationStatus.Valid or SpecValidationStatus.ValidWithWarnings;

    public static SpecValidationResult Invalid(params SpecDiagnostic[] diagnostics) =>
        new(SpecValidationStatus.Invalid, null, null, null, diagnostics, 0);
}

public interface ISpecValidator
{
    /// <summary>
    /// Parses and policy-checks a document. Never throws on malformed input — a bad spec
    /// is an expected outcome that becomes an <see cref="SpecValidationStatus.Invalid"/> result.
    /// </summary>
    /// <param name="strict">
    /// Also apply <see cref="StrictSdkPolicy"/> — the checks that decide whether a usable SDK
    /// can come out — and treat the parser's own warnings as errors.
    /// </param>
    /// <param name="previousApiVersion">
    /// info.version of the newest document that came before this one, when there is one. The
    /// only piece of the project's history a document has to be judged against: a registry is
    /// append-only, so a version that does not move forward can never be published.
    /// </param>
    Task<SpecValidationResult> ValidateAsync(
        Stream content,
        string fileName,
        bool strict = false,
        string? previousApiVersion = null,
        CancellationToken ct = default);
}
