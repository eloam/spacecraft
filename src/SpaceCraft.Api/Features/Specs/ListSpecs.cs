using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Specs;

/// <summary>
/// A page of versions and how many exist. Without the total, a project past the cap would be
/// shown its hundred most recent as though that were all of them.
/// </summary>
public sealed record SpecListResponse(IReadOnlyList<SpecVersionResponse> Items, int Total);

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
