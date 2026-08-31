using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Pipeline.Building;

public sealed record BuildRequest
{
    /// <summary>Absolute host path of the run workspace, bind-mounted into the sandbox.</summary>
    public required string WorkspaceRoot { get; init; }

    /// <summary>Source directory, relative to the workspace root.</summary>
    public required string SourceDirectory { get; init; }

    /// <summary>Directory the packaged artifact is written to, relative to the workspace root.</summary>
    public required string ArtifactDirectory { get; init; }

    public required string PackageName { get; init; }

    public required string Version { get; init; }
}

public sealed record BuildOutcome
{
    public required bool Succeeded { get; init; }

    public required string Output { get; init; }

    public string? FailureReason { get; init; }

    /// <summary>Artifact file name inside the artifact directory, on success.</summary>
    public string? ArtifactFileName { get; init; }

    /// <summary>
    /// What the builder packed, so the bytes are stored and served as what they are.
    /// </summary>
    /// <remarks>
    /// The builder is the only thing that knows: npm packs a gzipped tarball, while a .nupkg,
    /// a .jar and a .whl are all zip containers. The type was hard-coded to application/gzip
    /// where the artifact is stored and again where it is served, so every non-npm language
    /// would have described its package as something it is not — and a client that trusts the
    /// type over the file name would try to gunzip a zip.
    /// </remarks>
    public string ArtifactMediaType { get; init; } = "application/octet-stream";

    public static BuildOutcome Failed(string reason, string output) =>
        new() { Succeeded = false, FailureReason = reason, Output = output };
}

/// <summary>Compiles generated sources and packages them into a registry-ready artifact.</summary>
public interface ISdkBuilder
{
    SdkLanguage Language { get; }

    Task<BuildOutcome> BuildAsync(BuildRequest request, CancellationToken ct = default);
}
