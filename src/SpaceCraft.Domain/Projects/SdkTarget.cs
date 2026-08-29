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

    /// <summary>
    /// Which credential publishes this target, and so — since the endpoint lives on the
    /// credential — where its packages go.
    ///
    /// <para>
    /// Null means "the organization's one usable credential for this registry", which keeps
    /// the common case free of configuration. It stops being an answer as soon as there are
    /// two, and the publish step says so rather than picking: before this existed it took the
    /// most recently created one, so adding a credential silently redirected every target in
    /// the organization.
    /// </para>
    /// </summary>
    public Guid? RegistryCredentialId { get; private set; }

    /// <summary>Points the target at a credential, or back at whichever one is usable.</summary>
    public void UseCredential(Guid? registryCredentialId)
    {
        RegistryCredentialId = registryCredentialId;
        Touch();
    }

    /// <summary>
    /// Points the target at a different package.
    ///
    /// The last publication is forgotten along with the old name: it went out under a package
    /// this target no longer publishes to, and reporting it beside the new one would claim a
    /// release that was never made. Nothing is lost — the registry keeps what it was given,
    /// and the publication rows keep the name each version went out under.
    /// </summary>
    public void Rename(string packageName)
    {
        if (!PackageCoordinates.IsValidFor(Registry, packageName))
        {
            throw new ArgumentException($"'{packageName}' is not a valid package name for {Registry}.", nameof(packageName));
        }

        if (string.Equals(PackageName, packageName, StringComparison.Ordinal))
        {
            return;
        }

        PackageName = packageName;
        LastPublishedVersion = null;
        LastPublishedAt = null;
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
