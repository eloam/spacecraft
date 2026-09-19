using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Credentials;

internal static class CredentialQueries
{
    /// <summary>The targets that publish with a credential, by project.</summary>
    public static Task<List<CredentialUsageResponse>> UsedByAsync(
        Guid organizationId, Guid credentialId, SpaceCraftDbContext db, CancellationToken ct) =>
        // Ordered before the projection, not after: sorting on a record built by constructor
        // asks EF to map ProjectName back to a column it can no longer see, which it answers
        // by throwing. The write had already been committed by then, so a restore persisted
        // and still reported failure — correct after a refresh, wrong on screen.
        db.SdkTargets
            .AsNoTracking()
            .Where(t => t.RegistryCredentialId == credentialId)
            .Join(db.ApiProjects.Where(p => p.OrganizationId == organizationId),
                t => t.ApiProjectId, p => p.Id,
                (t, p) => new { Target = t, Project = p })
            .OrderBy(x => x.Project.Name).ThenBy(x => x.Target.PackageName)
            .Select(x => new CredentialUsageResponse(
                x.Target.Id, x.Target.PackageName, x.Target.Language,
                x.Project.Id, x.Project.Name, x.Project.Slug))
            .ToListAsync(ct);
}
