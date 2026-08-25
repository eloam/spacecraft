namespace SpaceCraft.Pipeline;

/// <summary>
/// How pipeline steps are isolated from the host.
/// <para>
/// This used to be a boolean, which forced a bad choice: deploying into a serverless job or
/// an ephemeral pod — where the platform already isolates the workload and nested containers
/// are not available — looked identical to switching the sandbox off on a laptop. Naming the
/// three situations separately lets the unsafe one stay refused while the deliberate one is
/// declared out loud.
/// </para>
/// </summary>
public enum SandboxMode
{
    /// <summary>
    /// Each command runs in a throwaway Docker container with dropped capabilities, a
    /// read-only root filesystem and bounded resources. Requires a Docker daemon, so it
    /// implies a VM or a privileged pod.
    /// </summary>
    Container = 0,

    /// <summary>
    /// Commands run directly, because the host is itself disposable and isolated — a
    /// Serverless Job, a single-use pod, a microVM. The operator asserts that the boundary
    /// exists one level up; the toolchains must be present in the image.
    /// </summary>
    PlatformIsolated = 1,

    /// <summary>
    /// Commands run directly on a shared machine with no boundary at all. Development only,
    /// and refused outside it.
    /// </summary>
    None = 2,
}

public static class SandboxModeExtensions
{
    /// <summary>True when steps are executed through nested containers rather than in-process.</summary>
    public static bool UsesContainers(this SandboxMode mode) => mode == SandboxMode.Container;
}
