using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Credentials;

public static class ListCredentials
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        // Two queries rather than one: a per-row subquery joining another table is not
        // something EF can translate, and the usages are a handful of rows for an
        // organization — cheaper to fetch once and stitch than to make the database repeat it.
        var credentials = await db.RegistryCredentials
            .AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        var usages = await db.SdkTargets
            .AsNoTracking()
            .Where(t => t.RegistryCredentialId != null)
            .Join(db.ApiProjects.Where(p => p.OrganizationId == organizationId),
                t => t.ApiProjectId, p => p.Id,
                (t, p) => new
                {
                    t.RegistryCredentialId,
                    Usage = new CredentialUsageResponse(
                        t.Id, t.PackageName, t.Language, p.Id, p.Name, p.Slug),
                })
            .ToListAsync(ct);

        var byCredential = usages
            .GroupBy(u => u.RegistryCredentialId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CredentialUsageResponse>)g
                    .Select(u => u.Usage)
                    .OrderBy(u => u.ProjectName)
                    .ThenBy(u => u.PackageName)
                    .ToList());

        var response = credentials
            .Select(c => new CredentialResponse(
                c.Id, c.Registry, c.RegistryUrl, c.Label, c.LastFour, c.IsRevoked,
                c.ExpiresAt, c.LastUsedAt, c.CreatedAt,
                byCredential.GetValueOrDefault(c.Id, [])))
            .ToList();

        return TypedResults.Ok(response);
    }
}
