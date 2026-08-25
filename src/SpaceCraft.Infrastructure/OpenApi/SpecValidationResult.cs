using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Infrastructure.OpenApi;

public enum DiagnosticSeverity
{
    Warning = 1,
    Error = 2,
}

/// <summary>One parser or policy finding, addressed to the operator who uploaded the spec.</summary>
public sealed record SpecDiagnostic(DiagnosticSeverity Severity, string Message, string? JsonPointer = null);

public sealed record SpecValidationResult(
    SpecValidationStatus Status,
    string? ApiTitle,
    string? ApiVersion,
    IReadOnlyList<SpecDiagnostic> Diagnostics,
    int OperationCount)
{
    public int WarningCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

    public int ErrorCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);

    public bool IsGeneratable => Status is SpecValidationStatus.Valid or SpecValidationStatus.ValidWithWarnings;

    public static SpecValidationResult Invalid(params SpecDiagnostic[] diagnostics) =>
        new(SpecValidationStatus.Invalid, null, null, diagnostics, 0);
}

public interface ISpecValidator
{
    /// <summary>
    /// Parses and policy-checks a document. Never throws on malformed input — a bad spec
    /// is an expected outcome that becomes an <see cref="SpecValidationStatus.Invalid"/> result.
    /// </summary>
    Task<SpecValidationResult> ValidateAsync(Stream content, string fileName, CancellationToken ct = default);
}
