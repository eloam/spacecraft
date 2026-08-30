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
}

/// <summary>
/// The guard every organization-scoped endpoint opens with.
/// </summary>
public static class OrganizationAccessGuard
{
    /// <summary>
    /// Returns the refusal to return, or null when the caller may proceed.
    /// <para>
    /// Written once rather than at each of the three dozen call sites so the two refusals
    /// cannot drift apart. They are genuinely different answers. A non-member gets 404,
    /// because confirming that an organization id is real would turn the API into a
    /// directory of them. A member whose role is too low gets 403 naming the role: nothing
    /// is left to conceal from someone already reading the organization, and telling them it
    /// does not exist contradicts the page they are standing on.
    /// </para>
    /// </summary>
    public static async Task<IResult?> DenyUnlessAsync(
        this IOrganizationAccess access,
        Guid organizationId,
        Guid userId,
        OrganizationRole required,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(access);

        var role = await access.GetRoleAsync(organizationId, userId, ct);

        if (role is null)
        {
            return ApiResults.NotFoundOrForbidden();
        }

        return role < required ? ApiResults.Forbidden(required) : null;
    }
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
}
