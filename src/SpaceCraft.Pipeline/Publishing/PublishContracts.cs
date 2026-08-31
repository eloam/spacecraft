using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Pipeline.Publishing;

public sealed record PublishRequest
{
    /// <summary>Absolute host path of the run workspace.</summary>
    public required string WorkspaceRoot { get; init; }

    /// <summary>Artifact directory, relative to the workspace root.</summary>
    public required string ArtifactDirectory { get; init; }

    /// <summary>File name of the tarball produced by the build step.</summary>
    public required string ArtifactFileName { get; init; }

    public required string PackageName { get; init; }

    public required string Version { get; init; }

    /// <summary>
    /// Where to publish. Comes from the credential, not from configuration: the token only
    /// authenticates against this registry, so the two travel together or neither means
    /// anything.
    /// </summary>
    public required string RegistryUrl { get; init; }

    /// <summary>
    /// Decrypted registry token. Held only for the duration of this call, never logged,
    /// and always listed in the sandbox's redaction set.
    /// </summary>
    public required string Token { get; init; }

    /// <summary>
    /// The user the token belongs to, where the registry asks for one. Null for npm and
    /// NuGet, which authenticate with the token alone.
    /// </summary>
    public string? Username { get; init; }
}

public enum PublishResultKind
{
    Succeeded = 1,

    /// <summary>
    /// The registry already had this version. Treated as success: registries are append-only,
    /// so a retried or duplicated run must converge rather than fail.
    /// </summary>
    AlreadyExists = 2,

    Failed = 3,
}

public sealed record PublishOutcome
{
    public required PublishResultKind Kind { get; init; }

    public required string Output { get; init; }

    public string? RegistryUrl { get; init; }

    public string? FailureReason { get; init; }

    public bool IsTerminalSuccess => Kind is PublishResultKind.Succeeded or PublishResultKind.AlreadyExists;

    public static PublishOutcome Failed(string reason, string output) =>
        new() { Kind = PublishResultKind.Failed, FailureReason = reason, Output = output };
}

/// <summary>Pushes a built artifact to a package registry.</summary>
public interface ISdkPublisher
{
    RegistryKind Registry { get; }

    Task<PublishOutcome> PublishAsync(PublishRequest request, CancellationToken ct = default);
}
