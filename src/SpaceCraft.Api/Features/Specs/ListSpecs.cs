using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Specs.Responses;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Specs;

public static class ListSpecs
{
    /// <summary>Most versions one listing returns. The total travels with them, so a project
    /// past this knows it is seeing a slice.</summary>
    private const int ListLimit = 100;

    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid projectId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct) is { } denied)
        {
            return denied;
        }

        var scoped = db.SpecVersions
            .AsNoTracking()
            .Where(s => s.ApiProjectId == projectId
                        && db.ApiProjects.Any(p => p.Id == projectId && p.OrganizationId == organizationId));

        var total = await scoped.CountAsync(ct);

        var versions = await scoped
            .OrderByDescending(s => s.Sequence)
            .Take(ListLimit)
            .ToListAsync(ct);

        return TypedResults.Ok(new SpecListResponse(versions.Select(SpecVersionResponse.From).ToList(), total));
    }
}
