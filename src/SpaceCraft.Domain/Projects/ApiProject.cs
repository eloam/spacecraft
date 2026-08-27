using SpaceCraft.Domain.Common;

namespace SpaceCraft.Domain.Projects;

/// <summary>Where a project's OpenAPI document comes from.</summary>
public enum SpecSource
{
    /// <summary>Operator uploads a .yaml/.json by hand.</summary>
    Upload = 1,

    /// <summary>A GitHub App installation pushes changes to the spec path.</summary>
    GitHub = 2,
}

/// <summary>
/// One API, tracked over time. Holds the history of its OpenAPI documents and the
/// set of SDKs SpaceCraft emits from them.
/// </summary>
public sealed class ApiProject : Entity
{
    private readonly List<SpecVersion> _specVersions = [];
    private readonly List<SdkTarget> _targets = [];

    private ApiProject() { }

    internal ApiProject(Guid organizationId, string name, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Common.Slug.IsValid(slug))
        {
            throw new ArgumentException($"'{slug}' is not a valid slug.", nameof(slug));
        }

        OrganizationId = organizationId;
        Name = name;
        Slug = slug;
    }

    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Changes the name and the slug with it, so the URL keeps matching what the project is
    /// called. Uniqueness within the organization is checked by
    /// <c>Organization.RenameProject</c>, which is the only caller that can see the siblings.
    /// </summary>
    public void Rename(string name)
    {
        Name = ResourceName.Require(name);
        Slug = Common.Slug.From(Name);
        Touch();
    }
    public string Slug { get; private set; } = null!;
    public SpecSource Source { get; private set; } = SpecSource.Upload;

    /// <summary>GitHub repository in "owner/repo" form, when <see cref="Source"/> is GitHub.</summary>
    public string? GitHubRepository { get; private set; }

    /// <summary>Path to the spec inside the repository, e.g. "docs/openapi.yaml".</summary>
    public string? GitHubSpecPath { get; private set; }

    /// <summary>Branch watched for spec changes.</summary>
    public string? GitHubBranch { get; private set; }

    /// <summary>
    /// Whether uploads are held to the checks that decide whether a usable SDK can come out,
    /// on top of "is this valid OpenAPI".
    ///
    /// On by default: a project exists here to publish an SDK, so a document that cannot
    /// produce a working one should say so at upload rather than after a build. It is per
    /// project because the answer differs — a spec you own can be fixed, one you only consume
    /// cannot, and there the checks are noise standing between you and a usable client.
    /// </summary>
    public bool StrictValidation { get; private set; } = true;

    public void SetStrictValidation(bool enabled)
    {
        if (StrictValidation == enabled) return;

        StrictValidation = enabled;
        Touch();
    }

    public IReadOnlyCollection<SpecVersion> SpecVersions => _specVersions;
    public IReadOnlyCollection<SdkTarget> Targets => _targets;

    public void ConnectGitHub(string repository, string specPath, string branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(specPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        Source = SpecSource.GitHub;
        GitHubRepository = repository;
        GitHubSpecPath = specPath;
        GitHubBranch = branch;
        Touch();
    }

    public void DisconnectGitHub()
    {
        Source = SpecSource.Upload;
        GitHubRepository = null;
        GitHubSpecPath = null;
        GitHubBranch = null;
        Touch();
    }

    /// <summary>
    /// Registers a newly stored OpenAPI document. Sequence numbers are per-project and
    /// monotonic so the UI can show "v4 of this spec" without exposing storage keys.
    /// </summary>
    public SpecVersion AddSpecVersion(
        Func<Guid, string> storageKeyFactory,
        string contentHash,
        long sizeInBytes,
        SpecSource source)
    {
        var sequence = _specVersions.Count == 0 ? 1 : _specVersions.Max(s => s.Sequence) + 1;
        var version = new SpecVersion(Id, sequence, storageKeyFactory, contentHash, sizeInBytes, source);
        _specVersions.Add(version);
        Touch();
        return version;
    }

    public SdkTarget AddTarget(SdkLanguage language, string packageName, RegistryKind registry)
    {
        if (_targets.Any(t => t.Language == language))
        {
            throw new InvalidOperationException($"This project already targets {language}.");
        }

        if (!PackageCoordinates.IsValidFor(registry, packageName))
        {
            throw new ArgumentException($"'{packageName}' is not a valid package name for {registry}.", nameof(packageName));
        }

        var target = new SdkTarget(Id, language, packageName, registry);
        _targets.Add(target);
        Touch();
        return target;
    }

    public SpecVersion? LatestSpec => _specVersions.Count == 0
        ? null
        : _specVersions.OrderByDescending(s => s.Sequence).First();
}
