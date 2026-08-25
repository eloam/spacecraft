using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Pipeline;

public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    /// <summary>
    /// How steps are isolated from the host. Defaults to nested containers; see
    /// <see cref="SandboxMode"/> for when to declare something else.
    /// </summary>
    public SandboxMode Sandbox { get; set; } = SandboxMode.Container;

    /// <summary>Root directory for per-run workspaces.</summary>
    [Required]
    public string WorkspaceRoot { get; set; } = Path.Combine(Path.GetTempPath(), "spacecraft-workspaces");

    /// <summary>
    /// Image carrying the Kiota CLI. Pinned, never :latest.
    /// <para>
    /// The generator and its per-language runtime packages are a matched pair. MCR's
    /// :latest tag currently resolves to 1.29.1, which emits
    /// <c>getCollectionOfPrimitiveValues&lt;string&gt;()</c> while recommending abstractions
    /// preview.103 — where that method requires an argument. Every generated SDK using a
    /// primitive array then fails to compile. Pinning is what keeps the pair consistent.
    /// </para>
    /// </summary>
    public string KiotaImage { get; set; } = "mcr.microsoft.com/openapi/kiota:1.34.1";

    /// <summary>Image carrying the Node toolchain used to build and publish npm packages.</summary>
    public string NodeImage { get; set; } = "node:22-alpine";

    /// <summary>Registry npm publishes to. Overridden in tests to point at a local Verdaccio.</summary>
    public string NpmRegistry { get; set; } = "https://registry.npmjs.org";

    public TimeSpan GenerateTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan BuildTimeout { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Keep run workspaces on disk after completion. Debugging aid.</summary>
    public bool RetainWorkspaces { get; set; }

    /// <summary>
    /// Fails fast at startup on a configuration that would run untrusted code unsandboxed
    /// outside development.
    /// </summary>
    public void Validate(bool isDevelopment)
    {
        if (Sandbox == SandboxMode.None && !isDevelopment)
        {
            throw new InvalidOperationException(
                "Pipeline:Sandbox is None outside Development. Generated SDK code and its "
                + "dependency install scripts would execute directly on a shared host with the "
                + "process's privileges. Use Container on a Docker host, or PlatformIsolated when "
                + "the workload already runs in a disposable, isolated environment such as a "
                + "serverless job. Refusing to start.");
        }

        // A floating generator tag silently breaks every generated SDK the day the image moves
        // ahead of (or behind) the runtime packages it recommends — that exact mismatch shipped
        // code that could not compile. Only the container mode pulls images; the other modes
        // expect the toolchain to be baked into the running image.
        if (Sandbox.UsesContainers() && UsesFloatingTag(KiotaImage))
        {
            throw new InvalidOperationException(
                $"Pipeline:KiotaImage is '{KiotaImage}'. The generator and its per-language runtime "
                + "packages are a matched pair, so the image must be pinned to an explicit version "
                + "(for example mcr.microsoft.com/openapi/kiota:1.34.1).");
        }
    }

    /// <summary>True for an untagged image or one tagged :latest.</summary>
    internal static bool UsesFloatingTag(string image)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            return true;
        }

        // Ignore a colon that belongs to a registry port (localhost:5000/kiota).
        var lastSlash = image.LastIndexOf('/');
        var tagSeparator = image.LastIndexOf(':');

        return tagSeparator <= lastSlash
            || image[(tagSeparator + 1)..].Equals("latest", StringComparison.OrdinalIgnoreCase);
    }
}
