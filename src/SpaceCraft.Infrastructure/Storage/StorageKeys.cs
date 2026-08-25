namespace SpaceCraft.Infrastructure.Storage;

/// <summary>
/// Every object is namespaced by organization id. Keeping the tenant in the key prefix
/// means a bucket policy or a lifecycle rule can be scoped per tenant later without
/// re-keying anything.
/// </summary>
public static class StorageKeys
{
    public static string Spec(Guid organizationId, Guid apiProjectId, Guid specVersionId, string extension) =>
        $"specs/{organizationId:N}/{apiProjectId:N}/{specVersionId:N}{NormalizeExtension(extension)}";

    public static string Artifact(Guid organizationId, Guid pipelineRunId, string fileName) =>
        $"artifacts/{organizationId:N}/{pipelineRunId:N}/{fileName}";

    public static string StepLog(Guid organizationId, Guid pipelineRunId, string stepName) =>
        $"logs/{organizationId:N}/{pipelineRunId:N}/{stepName.ToLowerInvariant()}.log";

    private static string NormalizeExtension(string extension) =>
        string.IsNullOrWhiteSpace(extension) ? string.Empty
        : extension.StartsWith('.') ? extension
        : "." + extension;
}
