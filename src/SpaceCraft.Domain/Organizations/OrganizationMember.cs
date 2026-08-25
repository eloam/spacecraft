using SpaceCraft.Domain.Common;

namespace SpaceCraft.Domain.Organizations;

public enum OrganizationRole
{
    /// <summary>Read-only: can view projects, specs and run history.</summary>
    Viewer = 1,

    /// <summary>Can upload specs and trigger pipeline runs, but not publish or manage credentials.</summary>
    Member = 2,

    /// <summary>Can configure targets, manage registry credentials and publish.</summary>
    Admin = 3,

    /// <summary>Full control, including billing and deleting the organization.</summary>
    Owner = 4,
}

public sealed class OrganizationMember : Entity
{
    private OrganizationMember() { }

    internal OrganizationMember(Guid organizationId, Guid userId, OrganizationRole role)
    {
        OrganizationId = organizationId;
        UserId = userId;
        Role = role;
    }

    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public OrganizationRole Role { get; private set; }

    public void ChangeRole(OrganizationRole role)
    {
        Role = role;
        Touch();
    }

    public bool Can(OrganizationRole required) => Role >= required;
}
