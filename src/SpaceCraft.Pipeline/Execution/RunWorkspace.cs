namespace SpaceCraft.Pipeline.Execution;

/// <summary>
/// The scratch directory for a single pipeline run.
/// <para>
/// Every path handed to a sandboxed command is <em>relative</em> to the workspace root.
/// That is what lets the same command line work under the local runner (where the root is
/// a host directory) and under Docker (where the root is bind-mounted at /workspace).
/// </para>
/// </summary>
public sealed class RunWorkspace : IDisposable
{
    public const string SpecDirectoryName = "spec";
    public const string SourceDirectoryName = "out";
    public const string ArtifactDirectoryName = "artifact";

    private readonly bool _retain;

    private RunWorkspace(string root, bool retain)
    {
        Root = root;
        _retain = retain;
    }

    /// <summary>Absolute host path of the workspace root.</summary>
    public string Root { get; }

    public static string SpecRelativePath(string fileName) => $"{SpecDirectoryName}/{fileName}";

    public static string SourceRelativePath => SourceDirectoryName;

    public static string ArtifactRelativePath => ArtifactDirectoryName;

    public string HostPath(string relativePath) =>
        Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public static RunWorkspace Create(string workspaceRoot, Guid runId, bool retain)
    {
        var root = Path.Combine(workspaceRoot, runId.ToString("N"));

        Directory.CreateDirectory(Path.Combine(root, SpecDirectoryName));
        Directory.CreateDirectory(Path.Combine(root, SourceDirectoryName));
        Directory.CreateDirectory(Path.Combine(root, ArtifactDirectoryName));

        return new RunWorkspace(root, retain);
    }

    public IReadOnlyList<string> ListGeneratedFiles()
    {
        var sourceRoot = HostPath(SourceDirectoryName);
        return Directory.Exists(sourceRoot)
            ? Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
            : [];
    }

    public void Dispose()
    {
        if (_retain)
        {
            return;
        }

        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A retained handle should not fail the run; stale workspaces are swept separately.
        }
        catch (UnauthorizedAccessException)
        {
            // Container-created files can be owned by the sandbox uid on Linux hosts.
        }
    }
}
