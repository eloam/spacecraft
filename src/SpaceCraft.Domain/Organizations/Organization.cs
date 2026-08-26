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

    public Organization(string name, string slug, Guid createdByUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Common.Slug.IsValid(slug))
        {
            throw new ArgumentException($"'{slug}' is not a valid slug.", nameof(slug));
        }

        Name = name;
        Slug = slug;
        _members.Add(new OrganizationMember(Id, createdByUserId, OrganizationRole.Owner));
    }

    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;

    public IReadOnlyCollection<OrganizationMember> Members => _members;
    public IReadOnlyCollection<ApiProject> Projects => _projects;

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
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

    /// <summary>Refuses to strip the last owner, which would orphan the organization.</summary>
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
