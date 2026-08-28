using SpaceCraft.Domain.Common;

namespace SpaceCraft.Infrastructure.Storage;

/// <summary>
/// Every object lives under the project it belongs to, and every project under its
/// organization:
///
/// <code>
/// orgs/{org}/projects/{project}/specs/{name}-{spec}.yaml
/// orgs/{org}/projects/{project}/runs/{run}/artifact/{fileName}
/// orgs/{org}/projects/{project}/runs/{run}/logs/{step}.log
/// </code>
///
/// Grouping by ownership rather than by kind is what lets one prefix answer "what belongs to
/// this project" — for a listing, a lifecycle rule, or a bulk delete — which a bucket split
/// into specs/, artifacts/ and logs/ never could.
///
/// Ids are written with dashes. They cost four characters against the compact form and give
/// back keys that can be pasted straight into a query or a URL.
/// </summary>
public static class StorageKeys
{
    /// <summary>Used when an uploaded file name leaves nothing usable behind.</summary>
    private const string FallbackSpecName = "spec";

    public static string Spec(
        Guid organizationId,
        Guid apiProjectId,
        Guid specVersionId,
        string fileName,
        string extension) =>
        $"{Project(organizationId, apiProjectId)}/specs/"
        + $"{SafeName(fileName)}-{specVersionId:D}{NormalizeExtension(extension)}";

    public static string Artifact(
        Guid organizationId,
        Guid apiProjectId,
        Guid pipelineRunId,
        string fileName) =>
        $"{Run(organizationId, apiProjectId, pipelineRunId)}/artifact/{fileName}";

    public static string StepLog(
        Guid organizationId,
        Guid apiProjectId,
        Guid pipelineRunId,
        string stepName) =>
        $"{Run(organizationId, apiProjectId, pipelineRunId)}/logs/{stepName.ToLowerInvariant()}.log";

    /// <summary>
    /// The document's format, read back from the key that stored it. The extension is fixed at
    /// upload from the one validation accepted, so the key is the record of what was uploaded
    /// — there is no separate column to disagree with.
    /// </summary>
    public static string FormatOf(string storageKey) =>
        Path.GetExtension(storageKey).Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? "json"
            : "yaml";

    /// <summary>Everything a project owns sits under this prefix.</summary>
    public static string Project(Guid organizationId, Guid apiProjectId) =>
        $"orgs/{organizationId:D}/projects/{apiProjectId:D}";

    private static string Run(Guid organizationId, Guid apiProjectId, Guid pipelineRunId) =>
        $"{Project(organizationId, apiProjectId)}/runs/{pipelineRunId:D}";

    /// <summary>
    /// Reduces an uploaded file name to something safe to put in a key.
    ///
    /// The name arrives from the client and is never trusted: browsers send a bare name, but
    /// nothing stops a caller sending "../../etc/passwd" or a full Windows path, and a key is
    /// a path. Path.GetFileName alone is not enough — it splits on the host's separator only,
    /// so a backslash survives on Linux.
    ///
    /// What comes out of <see cref="Slug"/> is [a-z0-9-], capped, with no separator left to
    /// climb out of the prefix with.
    /// </summary>
    private static string SafeName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return FallbackSpecName;

        var lastSeparator = fileName.LastIndexOfAny(['/', '\\']);
        var leaf = lastSeparator >= 0 ? fileName[(lastSeparator + 1)..] : fileName;

        // The extension is appended separately, from the one validation accepted, so keeping
        // it here would produce "spec-yaml-{guid}.yaml".
        var stem = Path.GetFileNameWithoutExtension(leaf);

        if (string.IsNullOrWhiteSpace(stem)) return FallbackSpecName;

        try
        {
            return Slug.From(stem);
        }
        catch (ArgumentException)
        {
            // How Slug reports a name it cannot reduce: "---", or one written entirely in a
            // script it strips, like 名前. Neither is a reason to refuse an upload, and no
            // cheaper predicate exists — telling those apart means running the same
            // normalisation Slug already does.
            return FallbackSpecName;
        }
    }

    private static string NormalizeExtension(string extension) =>
        string.IsNullOrWhiteSpace(extension) ? string.Empty
        : extension.StartsWith('.') ? extension
        : "." + extension;
}
