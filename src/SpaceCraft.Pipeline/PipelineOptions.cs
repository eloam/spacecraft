using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Pipeline;

public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    /// <summary>Root directory for per-run workspaces.</summary>
    [Required]
    public string WorkspaceRoot { get; set; } = Path.Combine(Path.GetTempPath(), "spacecraft-workspaces");

    /// <summary>Registry npm publishes to. Overridden in tests to point at a local Verdaccio.</summary>
    [Required]
    public string NpmRegistry { get; set; } = "https://registry.npmjs.org";

    public TimeSpan GenerateTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan BuildTimeout { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Keep run workspaces on disk after completion. Debugging aid.</summary>
    public bool RetainWorkspaces { get; set; }

}
