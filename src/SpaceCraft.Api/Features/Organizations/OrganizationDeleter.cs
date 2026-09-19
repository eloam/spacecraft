using SpaceCraft.Api.Common;
using SpaceCraft.Api.Features.Runs;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Organizations;

/// <summary>
/// Deletes an organization along with everything it owns.
/// </summary>
/// <remarks>
/// Cascade deletes cover only part of the graph: projects, members, targets and specs hang
/// off the organization by foreign key, but pipeline runs, registry credentials, publications
/// and invitations carry its id without a constraint behind it. Relying on the database alone
/// would leave those behind — including encrypted registry tokens outliving the organization
/// they belonged to, which is the one kind of leftover that actually matters, and pending
/// invitations that AcceptInvitation would have to notice point at nothing.
/// </remarks>
public sealed class OrganizationDeleter(
    SpaceCraftDbContext db,
    IObjectStorage storage,
    RunDeleter runs,
    ILogger<OrganizationDeleter> logger)
{
    public async Task<DeletionRefusal?> DeleteAsync(Guid organizationId, CancellationToken ct)
    {
        if (await runs.RefuseWhileActiveAsync(r => r.OrganizationId == organizationId, ct) is { } refusal)
        {
            return refusal;
        }

        // The specs go with their projects by foreign key; the documents behind them do not.
        var specKeys = await db.SpecVersions
            .Where(s => db.ApiProjects.Any(p => p.Id == s.ApiProjectId && p.OrganizationId == organizationId))
            .Select(s => s.StorageKey)
            .ToListAsync(ct);

        var runKeys = await runs.RemoveAsync(r => r.OrganizationId == organizationId, ct);

        // No foreign key covers these three, so nothing would remove them for us.
        db.RegistryCredentials.RemoveRange(
            await db.RegistryCredentials.Where(c => c.OrganizationId == organizationId).ToListAsync(ct));
        db.Publications.RemoveRange(
            await db.Publications.Where(p => p.OrganizationId == organizationId).ToListAsync(ct));
        db.OrganizationInvitations.RemoveRange(
            await db.OrganizationInvitations.Where(i => i.OrganizationId == organizationId).ToListAsync(ct));

        var projects = await db.ApiProjects.CountAsync(p => p.OrganizationId == organizationId, ct);

        var organization = await db.Organizations
            .Include(o => o.Members)
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        if (organization is not null)
        {
            db.Organizations.Remove(organization);
        }

        // The rows first, as one transaction; the objects only once it has landed. Removed
        // before the save, a save that failed or a request that was cancelled left rows
        // whose documents, artifacts and logs were already gone.
        await db.SaveChangesAsync(ct);

        await storage.TryDeleteAsync(specKeys.Concat(runKeys), logger);

        logger.LogWarning("Organization {OrganizationId} deleted with {Projects} project(s)",
            organizationId, projects);

        return null;
    }
}
