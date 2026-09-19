using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Runs;

public static class GetArtifact
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid runId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        IObjectStorage storage,
        CancellationToken ct)
    {
        // Viewer, like every other read of this run — its detail, and the step logs that
        // describe how this very tarball was built. Member was a reasonable floor while this
        // handed out a presigned bucket URL, which is a credential rather than a read; now
        // that the bytes are streamed, holding the artifact above the logs that narrate it
        // guarded nothing and left the button on the run page refusing a whole role class.
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct) is { } denied)
        {
            return denied;
        }

        var run = await db.PipelineRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId && r.OrganizationId == organizationId, ct);

        if (run?.ArtifactStorageKey is null)
        {
            return ApiResults.NotFound("An artifact for this run");
        }

        try
        {
            // Streamed through the API rather than handed out as a presigned bucket URL, the
            // way spec content and step logs already are. A presigned URL is a bearer token
            // in a link: anyone holding it reads the object for as long as it lasts, from a
            // bucket that then has to be reachable from a browser. Proxying costs this
            // process the bytes and buys a download that is authorized per request, against
            // a bucket with no public surface and no CORS policy at all.
            var content = await storage.GetAsync(run.ArtifactStorageKey, ct);

            // The key's last segment is the name the runner packed the tarball under, so the
            // saved file keeps it instead of being named after the run's id.
            var fileName = run.ArtifactStorageKey[(run.ArtifactStorageKey.LastIndexOf('/') + 1)..];

            // Read from the name rather than assumed. Hard-coding gzip was true of npm's
            // tarball and false of every other package this platform will build.
            return TypedResults.File(content, ArtifactMediaTypes.ForFileName(fileName), fileName);
        }
        catch (ObjectNotFoundException)
        {
            // The row names an artifact the bucket no longer holds — a lifecycle rule that
            // swept it, or a restore that missed it. Not a server error to the caller.
            return ApiResults.NotFound("An artifact for this run");
        }
    }
}
