using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.OpenApi;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Storage;
using SpaceCraft.Api.Features.Specs.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Specs;

/// <summary>
/// <paramref name="Strict"/> is nullable so that omitting it means "whatever the project is
/// set to" rather than "off": a caller that just wants today's rules replayed should not have
/// to know which mode the project runs in to ask for it.
/// </summary>
public sealed record RevalidateRequest(bool? Strict);

/// <summary>
/// Runs today's validator over a document validated by an older one.
///
/// Findings are written once, at upload, and read back from the row — so a spec keeps
/// whatever the rules said on the day it arrived. Every change to those rules would
/// otherwise apply only to documents uploaded afterwards, and re-uploading the same file
/// is no way out: identical bytes are recognised as a duplicate and skip validation
/// entirely. This is the way to bring an existing version up to date.
///
/// It deliberately does not trigger runs. Nothing about the contract changed, only what
/// we noticed about it, and republishing a package on that basis would be wrong.
/// </summary>
public static class RevalidateSpec
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid projectId,
        Guid specVersionId,
        [FromBody] RevalidateRequest? request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        ISpecValidator validator,
        IObjectStorage storage,
        CancellationToken ct)
    {
        // Member, not Viewer: this rewrites what the project records about a document.
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Member, ct) is { } denied)
        {
            return denied;
        }

        var project = await db.ApiProjects
            .Include(p => p.SpecVersions)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OrganizationId == organizationId, ct);

        var specVersion = project?.SpecVersions.FirstOrDefault(s => s.Id == specVersionId);

        if (project is null || specVersion is null)
        {
            return ApiResults.NotFound("The specification version");
        }

        if (!await storage.ExistsAsync(specVersion.StorageKey, ct))
        {
            return ApiResults.NotFound("The stored document");
        }

        // Buffered rather than validated straight off the storage stream: that one cannot
        // seek, and reading the declared OpenAPI version means going back to the top of the
        // document after the parser has run through it. Upload buffers for the same reason.
        using var buffer = new MemoryStream();
        await using (var stored = await storage.GetAsync(specVersion.StorageKey, ct))
        {
            await stored.CopyToAsync(buffer, ct);
        }

        buffer.Position = 0;

        // The validator picks its reader from the extension, and the storage key carries the
        // one the document was uploaded with.
        // The document that came before this one, not the newest: revalidating spec #1 must
        // not judge it against #2.
        var previousApiVersion = project.SpecVersions
            .Where(s => s.Sequence < specVersion.Sequence)
            .OrderByDescending(s => s.Sequence)
            .Select(s => s.ApiVersion)
            .FirstOrDefault();

        // Answering "what would the other mode say about this document?" without making the
        // project live in that mode. The findings it writes are the ones this run produced,
        // so the caller is told which mode ran and can replay the default to get it back.
        var strict = request?.Strict ?? project.StrictValidation;

        var validation = await validator.ValidateAsync(
            buffer, specVersion.StorageKey, strict, previousApiVersion, ct);

        specVersion.MarkValidated(
            status: validation.Status,
            apiTitle: validation.ApiTitle,
            apiVersion: validation.ApiVersion,
            openApiVersion: validation.OpenApiVersion,
            informationCount: validation.InformationCount,
            warningCount: validation.WarningCount,
            errorCount: validation.ErrorCount,
            diagnosticsJson: System.Text.Json.JsonSerializer.Serialize(validation.Diagnostics));

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(
            new SpecUploadResponse(SpecVersionResponse.From(specVersion), validation.Diagnostics, [], strict));
    }
}
