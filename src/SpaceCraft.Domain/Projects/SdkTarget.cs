using SpaceCraft.Domain.Common;

namespace SpaceCraft.Domain.Projects;

/// <summary>
/// "Emit this API as <c>@acme/billing-sdk</c> on npm." One per language per project.
/// </summary>
public sealed class SdkTarget : Entity
{
    private SdkTarget() { }

    internal SdkTarget(Guid apiProjectId, SdkLanguage language, string packageName, RegistryKind registry)
    {
        ApiProjectId = apiProjectId;
        Language = language;
        PackageName = packageName;
        Registry = registry;
    }

    public Guid ApiProjectId { get; private set; }
    public SdkLanguage Language { get; private set; }
    public string PackageName { get; private set; } = null!;
    public RegistryKind Registry { get; private set; }

    /// <summary>
    /// When true, a new spec version triggers generate + build + publish with no human step.
    /// Off by default: publishing to a public registry is irreversible.
    /// </summary>
    public bool AutoPublish { get; private set; }

    /// <summary>Last version successfully pushed to the registry, if any.</summary>
    public string? LastPublishedVersion { get; private set; }

    public DateTimeOffset? LastPublishedAt { get; private set; }

    public void Rename(string packageName)
    {
        if (!PackageCoordinates.IsValidFor(Registry, packageName))
        {
            throw new ArgumentException($"'{packageName}' is not a valid package name for {Registry}.", nameof(packageName));
        }

        PackageName = packageName;
        Touch();
    }

    public void SetAutoPublish(bool enabled)
    {
        AutoPublish = enabled;
        Touch();
    }

    public void RecordPublication(string version, DateTimeOffset at)
    {
        LastPublishedVersion = version;
        LastPublishedAt = at;
        Touch();
    }
}
