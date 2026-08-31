namespace SpaceCraft.Infrastructure.Storage;

/// <summary>
/// What a stored artifact is, read from the name it was stored under.
/// </summary>
/// <remarks>
/// The builder states the type when it writes the object, which is the authoritative answer.
/// Serving it back happens from the run row, which remembers only the storage key — so the
/// type is derived from the extension rather than by adding a column to carry what the file
/// name already says.
/// <para>
/// It matters beyond tidiness: the type used to be hard-coded to application/gzip in both
/// places. That is true of an npm tarball and false of every other language's package — a
/// .nupkg, a .jar and a .whl are zip containers, and a client that trusts the declared type
/// over the extension would try to gunzip a zip.
/// </para>
/// </remarks>
public static class ArtifactMediaTypes
{
    public const string Unknown = "application/octet-stream";

    public static string ForFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Unknown;
        }

        var name = fileName.AsSpan();

        return
            name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
                ? "application/gzip"
            // Every other package format this platform can produce is a zip container.
            : name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".whl", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? "application/zip"
            : Unknown;
    }
}
