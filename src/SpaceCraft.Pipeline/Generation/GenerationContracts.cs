using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Pipeline.Generation;

/// <summary>Everything the generator needs to turn one spec into one SDK source tree.</summary>
public sealed record GenerationRequest
{
    /// <summary>Absolute host path of the run workspace, bind-mounted into the sandbox.</summary>
    public required string WorkspaceRoot { get; init; }

    /// <summary>Spec path, relative to the workspace root.</summary>
    public required string SpecFilePath { get; init; }

    /// <summary>Output directory for generated sources, relative to the workspace root.</summary>
    public required string OutputDirectory { get; init; }

    public required SdkLanguage Language { get; init; }

    /// <summary>Registry package name, e.g. "@acme/billing-sdk".</summary>
    public required string PackageName { get; init; }

    /// <summary>Version the package is published as, taken from the spec's info.version.</summary>
    public required string Version { get; init; }

    /// <summary>Root client type name, derived from the API title when not given.</summary>
    public string? ClientClassName { get; init; }

    public string? ApiTitle { get; init; }

    public string? ApiDescription { get; init; }
}

public sealed record GenerationOutcome
{
    public required bool Succeeded { get; init; }

    public required string Output { get; init; }

    public string? FailureReason { get; init; }

    /// <summary>Number of source files emitted, for the run summary.</summary>
    public int FileCount { get; init; }

    public static GenerationOutcome Failed(string reason, string output) =>
        new() { Succeeded = false, FailureReason = reason, Output = output };
}

/// <summary>
/// Emits SDK sources for one language.
/// <para>
/// The abstraction is the point: SpaceCraft's value is orchestration, versioning and
/// publication, not the code emitter. Swapping Kiota for openapi-generator — or a
/// hand-written emitter for one language — should touch only an implementation of this.
/// </para>
/// </summary>
public interface ISdkGenerator
{
    SdkLanguage Language { get; }

    Task<GenerationOutcome> GenerateAsync(GenerationRequest request, CancellationToken ct = default);
}
