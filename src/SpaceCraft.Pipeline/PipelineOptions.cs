using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Pipeline;

public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    /// <summary>
    /// How long each step may run, in all. Constants rather than settings, and the reason is
    /// the relation nobody can tune around: <see cref="SpaceCraft.Domain.Pipelines.PipelineRun.LeaseDuration"/>
    /// has to outlast the longest of these, or a run near its limit is a race with the sweep
    /// that recovers dead workers. A setting could be raised past the lease in one environment
    /// and nothing would say so; a constant is seen by the test that pins the relation.
    /// </summary>
    public static readonly TimeSpan GenerateTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The whole build step's budget, shared by its install, compile and pack commands. Each
    /// gets what the ones before it left, which is what keeps the step under the lease.
    /// </summary>
    public static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Root directory for per-run workspaces.</summary>
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
    public string DependencyRegistry { get; set; } = "https://registry.npmjs.org";

    /// <summary>Keep run workspaces on disk after completion. Debugging aid.</summary>
    public bool RetainWorkspaces { get; set; }

    /// <summary>
    /// Acknowledges that pipeline steps run unsandboxed, in the worker's own process.
    /// </summary>
    /// <remarks>
    /// There is one <c>ICommandRunner</c> and it starts a child process on the host. The
    /// isolation the design relies on is the job's — one execution per container, torn down
    /// afterwards — and that now holds by construction: the runner takes a single run id and
    /// has no mode that claims a second. It used to hold only while somebody remembered to
    /// keep a setting at 1, which is not the same thing.
    /// <para>
    /// That may be an acceptable trade at this stage, but it should be a decision somebody
    /// took rather than a default nobody read. Outside Development, startup refuses unless
    /// this says yes.
    /// </para>
    /// </remarks>
    public bool AllowUnsandboxedExecution { get; set; }

}
