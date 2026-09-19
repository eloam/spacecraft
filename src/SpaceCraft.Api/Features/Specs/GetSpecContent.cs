using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Storage;
using SpaceCraft.Api.Features.Specs.Responses;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Specs;

public static class GetSpecContent
{
    public static async Task<Results<Ok<SpecContentResponse>, ProblemHttpResult>> HandleAsync(
        Guid organizationId,
        Guid projectId,
        Guid specVersionId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        IObjectStorage storage,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct) is { } denied)
        {
            return denied;
        }

        var storageKey = await db.SpecVersions
            .AsNoTracking()
            .Where(s => s.Id == specVersionId
                        && s.ApiProjectId == projectId
                        && db.ApiProjects.Any(p => p.Id == projectId && p.OrganizationId == organizationId))
            .Select(s => s.StorageKey)
            .FirstOrDefaultAsync(ct);

        if (storageKey is null)
        {
            return ApiResults.NotFound("The specification version");
        }

        try
        {
            var content = await storage.GetTextAsync(storageKey, ct);
            return TypedResults.Ok(new SpecContentResponse(content, StorageKeys.FormatOf(storageKey)));
        }
        catch (ObjectNotFoundException)
        {
            // The row outlived its object — a bucket wiped between environments, say. Saying
            // so beats handing back an empty document that reads as a spec with no content.
            // Caught rather than checked first, as GetArtifact and GetStepLog do: one storage
            // call instead of two, and no window between them for the object to go missing.
            return ApiResults.NotFound("The stored document");
        }
    }
}
