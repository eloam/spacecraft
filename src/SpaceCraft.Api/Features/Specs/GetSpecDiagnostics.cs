using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.OpenApi;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Specs;

/// <summary>
/// Validation findings were only ever returned in the upload response, so reloading the
/// page lost them: a spec could say "413 warnings" with no way to read one. They are
/// stored as JSON on the row, so serving them is a matter of handing that back.
/// </summary>
public static class GetSpecDiagnostics
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid projectId,
        Guid specVersionId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct) is { } denied)
        {
            return denied;
        }

        // The organization is part of the lookup, not just of the permission check: without
        // it, a viewer of one organization could pass their own id with another's project id
        // and read findings they have no access to.
        var stored = await db.SpecVersions
            .AsNoTracking()
            .Where(s => s.Id == specVersionId
                        && s.ApiProjectId == projectId
                        && db.ApiProjects.Any(p => p.Id == projectId && p.OrganizationId == organizationId))
            .Select(s => s.Diagnostics)
            .FirstOrDefaultAsync(ct);

        if (stored is null)
        {
            return ApiResults.NotFound("The specification version");
        }

        var diagnostics = System.Text.Json.JsonSerializer.Deserialize<List<SpecDiagnostic>>(stored) ?? [];

        return TypedResults.Ok(diagnostics);
    }
}
