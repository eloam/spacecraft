using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Projects.Responses;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Projects;

public static class ListProjects
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct) is { } denied)
        {
            return denied;
        }

        var projects = await db.ApiProjects
            .AsNoTracking()
            .Where(p => p.OrganizationId == organizationId)
            .Include(p => p.Targets)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var counts = await db.SpecVersions
            .Where(s => db.ApiProjects.Any(p => p.Id == s.ApiProjectId && p.OrganizationId == organizationId))
            .GroupBy(s => s.ApiProjectId)
            .Select(g => new { ApiProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ApiProjectId, x => x.Count, ct);

        return TypedResults.Ok(projects
            .Select(p => ProjectResponse.From(p, counts.GetValueOrDefault(p.Id)))
            .ToList());
    }
}
