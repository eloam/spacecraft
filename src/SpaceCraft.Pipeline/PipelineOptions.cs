using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Pipeline;

public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    /// <summary>Root directory for per-run workspaces.</summary>
    [Required]
    public string WorkspaceRoot { get; set; } = Path.Combine(Path.GetTempPath(), "spacecraft-workspaces");

    /// <summary>
    /// Where <c>npm install</c> resolves the generated package's dependencies from.
    ///
    /// <para>
    /// Not where anything is published: that is on the credential, because it is the client's
    /// choice and a token only authenticates against one registry. The two were the same
    /// setting once, which worked only because Verdaccio proxies npmjs — and would have sent
    /// Kiota's runtime packages looking inside a customer's private registry.
    /// </para>
    /// </summary>
    [Required]
    public string DependencyRegistry { get; set; } = "https://registry.npmjs.org";

    public TimeSpan GenerateTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan BuildTimeout { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Keep run workspaces on disk after completion. Debugging aid.</summary>
    public bool RetainWorkspaces { get; set; }

}
