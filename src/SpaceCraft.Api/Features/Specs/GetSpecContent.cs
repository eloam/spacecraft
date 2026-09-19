using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Specs;

/// <summary>
/// The document as uploaded, so findings can be shown against the source they came from.
/// Served through the API rather than as a presigned URL: it saves a CORS policy on the
/// bucket, and the upload cap already bounds what this can return.
/// </summary>
public sealed record SpecContentResponse(string Content, string Format);

public static class GetSpecContent
{
    public static async Task<IResult> HandleAsync(
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

        if (!await storage.ExistsAsync(storageKey, ct))
        {
            // The row outlived its object — a bucket wiped between environments, say. Saying
            // so beats handing back an empty document that reads as a spec with no content.
            return ApiResults.NotFound("The stored document");
        }

        var content = await storage.GetTextAsync(storageKey, ct);

        return TypedResults.Ok(new SpecContentResponse(content, StorageKeys.FormatOf(storageKey)));
    }
}
