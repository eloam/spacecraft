using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Domain.Organizations;

/// <summary>
/// Tenant boundary. Every project, credential and pipeline run hangs off an organization —
/// retrofitting multi-tenancy later is far more painful than carrying it from day one.
/// </summary>
public sealed class Organization : Entity
{
    private readonly List<OrganizationMember> _members = [];
    private readonly List<ApiProject> _projects = [];

    private Organization() { }

    /// <summary>
    /// The name is held to <see cref="ResourceName"/> from the start, not only on rename: a
    /// name that would not slug predictably is refused wherever it arrives, so the aggregate
    /// never holds one whose slug it could not derive. The slug is accepted as given,
    /// valid, because a caller may choose one; see <see cref="Rename"/> for how long it lasts.
    /// </summary>
    public Organization(string name, string slug, Guid createdByUserId)
    {
        if (!Common.Slug.IsValid(slug))
        {
            throw new ArgumentException($"'{slug}' is not a valid slug.", nameof(slug));
        }

        Name = ResourceName.Require(name);
        Slug = slug;
        _members.Add(new OrganizationMember(Id, createdByUserId, OrganizationRole.Owner));
    }

    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;

    public IReadOnlyCollection<OrganizationMember> Members => _members;
    public IReadOnlyCollection<ApiProject> Projects => _projects;

    /// <summary>
    /// Renames the organization and regenerates its slug to match.
    /// <para>
    /// The slug is the URL, so it follows the name rather than drifting away from it. The
    /// cost is that links to the old slug stop resolving — which is why the name is
    /// restricted to characters that slug predictably, and why the UI says so before saving.
    /// </para>
    /// <para>
    /// That includes a slug chosen explicitly at creation: the first rename replaces it with
    /// the derived one. One rule rather than two — the settings page shows the slug as
    /// derived from the name and warns that renaming moves it, without a case for a slug
    /// somebody once typed — and a caller who wants the slug to survive a rename does not
    /// rename.
    /// </para>
    /// </summary>
    public void Rename(string name)
    {
        Name = ResourceName.Require(name);
        Slug = Common.Slug.From(Name);
        Touch();
    }

    /// <summary>
    /// Renames one of this organization's projects, refusing a name that would collide with
    /// another project's slug. Lives here because uniqueness is scoped to the organization,
    /// and the project alone cannot see its siblings.
    /// </summary>
    public void RenameProject(Guid projectId, string name)
    {
        var project = _projects.SingleOrDefault(p => p.Id == projectId)
            ?? throw new InvalidOperationException("Project is not part of this organization.");

        var slug = Common.Slug.From(ResourceName.Require(name));

        if (_projects.Any(p => p.Id != projectId && p.Slug == slug))
        {
            throw new InvalidOperationException(
                $"Another project already uses the slug '{slug}'.");
        }

        project.Rename(name);
        Touch();
    }

    public OrganizationMember AddMember(Guid userId, OrganizationRole role)
    {
        if (_members.Any(m => m.UserId == userId))
        {
            throw new InvalidOperationException("User is already a member of this organization.");
        }

        var member = new OrganizationMember(Id, userId, role);
        _members.Add(member);
        Touch();
        return member;
    }

    /// <summary>
    /// Changes a member's role, refusing to leave the organization without an owner.
    /// <para>
    /// The same invariant <see cref="RemoveMember"/> enforces: demoting the last owner
    /// strands the organization just as surely as removing them, so both routes to that
    /// state are closed here rather than in one caller.
    /// </para>
    /// </summary>
    public void ChangeMemberRole(Guid userId, OrganizationRole role)
    {
        var member = _members.SingleOrDefault(m => m.UserId == userId)
            ?? throw new InvalidOperationException("User is not a member of this organization.");

        if (member.Role == OrganizationRole.Owner
            && role != OrganizationRole.Owner
            && _members.Count(m => m.Role == OrganizationRole.Owner) == 1)
        {
            throw new InvalidOperationException(
                "This is the only owner. Promote someone else before changing this role.");
        }

        member.ChangeRole(role);
        Touch();
    }

    public void RemoveMember(Guid userId)
    {
        var member = _members.SingleOrDefault(m => m.UserId == userId)
            ?? throw new InvalidOperationException("User is not a member of this organization.");

        if (member.Role == OrganizationRole.Owner && _members.Count(m => m.Role == OrganizationRole.Owner) == 1)
        {
            throw new InvalidOperationException("An organization must keep at least one owner.");
        }

        _members.Remove(member);
        Touch();
    }

    public ApiProject AddProject(string name, string slug)
    {
        if (_projects.Any(p => p.Slug == slug))
        {
            throw new InvalidOperationException($"Project '{slug}' already exists in this organization.");
        }

        var project = new ApiProject(Id, name, slug);
        _projects.Add(project);
        Touch();
        return project;
    }
}
