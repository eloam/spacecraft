using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Account;

internal static class AccountQueries
{
    /// <summary>
    /// What stops an account from being deleted: every organization it is the only owner of.
    /// </summary>
    public static async Task<IReadOnlyList<DeletionBlocker>> DeletionBlockersAsync(
        SpaceCraftDbContext db, Guid userId, CancellationToken ct)
    {
        var ownerships = await db.OrganizationMembers
            .Where(m => m.UserId == userId && m.Role == OrganizationRole.Owner)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);

        if (ownerships.Count == 0)
        {
            return [];
        }

        var soleOwned = await db.OrganizationMembers
            .Where(m => ownerships.Contains(m.OrganizationId) && m.Role == OrganizationRole.Owner)
            .GroupBy(m => m.OrganizationId)
            .Where(g => g.Count() == 1)
            .Select(g => g.Key)
            .ToListAsync(ct);

        if (soleOwned.Count == 0)
        {
            return [];
        }

        return await db.Organizations
            .Where(o => soleOwned.Contains(o.Id))
            .Select(o => new DeletionBlocker(o.Name, "You are its only owner."))
            .ToListAsync(ct);
    }
}
