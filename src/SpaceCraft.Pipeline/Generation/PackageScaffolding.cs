using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Pipeline.Generation;

public sealed record ScaffoldRequest
{
    /// <summary>Absolute host path of the directory holding the generated sources.</summary>
    public required string SourceDirectory { get; init; }

    /// <summary>Absolute host path of the run workspace.</summary>
    public required string WorkspaceRoot { get; init; }

    /// <summary>Spec path relative to the workspace root, needed to query the generator.</summary>
    public required string SpecRelativePath { get; init; }

    public required string PackageName { get; init; }

    public required string Version { get; init; }

    public required string ClientClassName { get; init; }

    public string? ApiTitle { get; init; }

    public string? ApiDescription { get; init; }

    /// <summary>Registry the package will be published to, which affects metadata.</summary>
    public required RegistryKind Registry { get; init; }
}

/// <summary>
/// Wraps raw generated sources into something a registry will accept: a manifest, a build
/// configuration, and the documentation the landing page promises.
/// </summary>
public interface IPackageScaffolder
{
    SdkLanguage Language { get; }

    Task ScaffoldAsync(ScaffoldRequest request, CancellationToken ct = default);
}
