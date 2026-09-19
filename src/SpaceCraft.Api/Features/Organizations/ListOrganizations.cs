using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Organizations.Responses;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Organizations;

public static class ListOrganizations
{
    public static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        var userId = principal.Id();

        // Order before projecting: EF cannot translate a sort over a constructed record,
        // and silently falls back to throwing rather than to client evaluation.
        var organizations = await db.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Join(db.Organizations, m => m.OrganizationId, o => o.Id, (m, o) => new { m.Role, Organization = o })
            .OrderBy(x => x.Organization.Name)
            .Select(x => new OrganizationResponse(
                x.Organization.Id,
                x.Organization.Name,
                x.Organization.Slug,
                x.Role,
                db.ApiProjects.Count(p => p.OrganizationId == x.Organization.Id),
                x.Organization.CreatedAt))
            .ToListAsync(ct);

        return TypedResults.Ok(organizations);
    }
}
