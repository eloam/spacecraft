using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Common;

/// <summary>
/// Tenant authorisation.
/// <para>
/// Every endpoint that touches organization-scoped data goes through here. Membership is
/// checked against the database on each request rather than trusted from a claim, so
/// removing someone takes effect immediately instead of when their access token expires.
/// </para>
/// </summary>
public interface IOrganizationAccess
{
    /// <summary>
    /// Returns the caller's role in the organization, or null when they are not a member.
    /// A non-member and a missing organization are deliberately indistinguishable to the
    /// caller: probing for slugs should not reveal which ones exist.
    /// </summary>
    Task<OrganizationRole?> GetRoleAsync(Guid organizationId, Guid userId, CancellationToken ct = default);

    Task<bool> HasAtLeastAsync(Guid organizationId, Guid userId, OrganizationRole required, CancellationToken ct = default);
}

public sealed class OrganizationAccess(SpaceCraftDbContext db) : IOrganizationAccess
{
    public async Task<OrganizationRole?> GetRoleAsync(Guid organizationId, Guid userId, CancellationToken ct = default)
    {
        var membership = await db.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId)
            .Select(m => (OrganizationRole?)m.Role)
            .FirstOrDefaultAsync(ct);

        return membership;
    }

    public async Task<bool> HasAtLeastAsync(
        Guid organizationId,
        Guid userId,
        OrganizationRole required,
        CancellationToken ct = default)
    {
        var role = await GetRoleAsync(organizationId, userId, ct);
        return role is not null && role >= required;
    }
}
