using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Common;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Organizations;

public static class GetOrganization
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        var role = await access.GetRoleAsync(organizationId, principal.Id(), ct);
        if (role is null)
        {
            return ApiResults.NotFoundOrForbidden();
        }

        var organization = await db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        if (organization is null)
        {
            return ApiResults.NotFoundOrForbidden();
        }

        var projectCount = await db.ApiProjects.CountAsync(p => p.OrganizationId == organizationId, ct);

        return TypedResults.Ok(new OrganizationResponse(
            organization.Id,
            organization.Name,
            organization.Slug,
            role.Value,
            projectCount,
            organization.CreatedAt));
    }
}
