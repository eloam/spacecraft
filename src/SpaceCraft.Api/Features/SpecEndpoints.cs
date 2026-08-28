using System.Security.Claims;
using System.Security.Cryptography;
using SpaceCraft.Api.Common;
using SpaceCraft.Api.Features.Deletion;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Infrastructure.OpenApi;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features;

public sealed record SpecVersionResponse(
    Guid Id,
    int Sequence,
    SpecValidationStatus ValidationStatus,
    string? ApiTitle,
    string? ApiVersion,
    string? OpenApiVersion,
    /// <summary>"yaml" or "json" — the notation the document was uploaded in.</summary>
    string Format,
    int InformationCount,
    int WarningCount,
    int ErrorCount,
    long SizeInBytes,
    SpecSource Source,
    DateTimeOffset CreatedAt);

/// <summary>
/// The document as uploaded, so findings can be shown against the source they came from.
/// Served through the API rather than as a presigned URL: it saves a CORS policy on the
/// bucket, and the upload cap already bounds what this can return.
/// </summary>
public sealed record SpecContentResponse(string Content, string Format);

/// <summary>
/// A page of versions and how many exist. Without the total, a project past the cap would be
/// shown its hundred most recent as though that were all of them.
/// </summary>
public sealed record SpecListResponse(IReadOnlyList<SpecVersionResponse> Items, int Total);

public sealed record SpecUploadResponse(
    SpecVersionResponse SpecVersion,
    IReadOnlyList<SpecDiagnostic> Diagnostics,
    IReadOnlyList<Guid> TriggeredRunIds);

public static class SpecEndpoints
{
    /// <summary>
    /// Matches <see cref="SpecLimits.MaxSizeInBytes"/>. Enforced by the server before the
    /// body is buffered, so an oversized upload is rejected without being read into memory.
    /// </summary>
    private const long MaxUploadBytes = SpecLimits.MaxSizeInBytes;

    /// <summary>Most versions one listing returns. The total travels with them, so a project
    /// past this knows it is seeing a slice.</summary>
    private const int ListLimit = 100;

    public static RouteGroupBuilder MapSpecEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations/{organizationId:guid}/projects/{projectId:guid}/specs")
            .WithTags("Specs")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .Produces<SpecListResponse>()
            .WithSummary("Lists the project's spec versions, newest first.");

        group.MapGet("/{specVersionId:guid}/diagnostics", GetDiagnosticsAsync)
            .Produces<List<SpecDiagnostic>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns the validation findings recorded when the spec was uploaded.");

        group.MapGet("/{specVersionId:guid}/content", GetContentAsync)
            .Produces<SpecContentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns the document as uploaded, for showing findings in context.");

        group.MapPost("/{specVersionId:guid}/revalidate", RevalidateAsync)
            .Produces<SpecUploadResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Re-runs validation over a stored document and replaces its findings.");

        group.MapDelete("/{specVersionId:guid}", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Deletes a spec version, unless a package was published from it.");

        group.MapPost("/", UploadAsync)
            .Produces<SpecUploadResponse>(StatusCodes.Status201Created)
            // Identical bytes are refused: nothing would change, and the old response looked
            // like a successful upload of a version it had not created.
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem()
            .WithSummary("Uploads an OpenAPI document as a new spec version.")
            // These two return IEndpointConventionBuilder rather than RouteHandlerBuilder,
            // which ends the typed chain — so they come last.
            .WithRequestTimeout(TimeSpan.FromMinutes(2))
            .DisableAntiforgery();

        return api;
    }

    private static async Task<IResult> ListAsync(
        Guid organizationId,
        Guid projectId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct))
        {
            return ApiResults.NotFoundOrForbidden();
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

        return TypedResults.Ok(new SpecListResponse(versions.Select(ToResponse).ToList(), total));
    }

    /// <summary>
    /// Validation findings were only ever returned in the upload response, so reloading the
    /// page lost them: a spec could say "413 warnings" with no way to read one. They are
    /// stored as JSON on the row, so serving them is a matter of handing that back.
    /// </summary>
    private static async Task<IResult> GetDiagnosticsAsync(
        Guid organizationId,
        Guid projectId,
        Guid specVersionId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct))
        {
            return ApiResults.NotFoundOrForbidden();
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

    private static async Task<IResult> GetContentAsync(
        Guid organizationId,
        Guid projectId,
        Guid specVersionId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        IObjectStorage storage,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct))
        {
            return ApiResults.NotFoundOrForbidden();
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
    private static async Task<IResult> RevalidateAsync(
        Guid organizationId,
        Guid projectId,
        Guid specVersionId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        ISpecValidator validator,
        IObjectStorage storage,
        CancellationToken ct)
    {
        // Member, not Viewer: this rewrites what the project records about a document.
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Member, ct))
        {
            return ApiResults.NotFoundOrForbidden();
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

        var validation = await validator.ValidateAsync(
            buffer, specVersion.StorageKey, project.StrictValidation, previousApiVersion, ct);

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

        return TypedResults.Ok(new SpecUploadResponse(ToResponse(specVersion), validation.Diagnostics, []));
    }

    private static async Task<IResult> UploadAsync(
        Guid organizationId,
        Guid projectId,
        IFormFile file,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        ISpecValidator validator,
        IObjectStorage storage,
        ResourceDeleter deleter,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Member, ct))
        {
            return ApiResults.NotFoundOrForbidden();
        }

        if (file is null || file.Length == 0)
        {
            return ApiResults.Invalid(nameof(file), "Attach an OpenAPI document.");
        }

        if (file.Length > MaxUploadBytes)
        {
            return ApiResults.Invalid(
                nameof(file),
                $"The document is larger than the {MaxUploadBytes / 1024 / 1024} MB limit.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".yaml" or ".yml" or ".json"))
        {
            return ApiResults.Invalid(nameof(file), "Upload a .yaml, .yml or .json document.");
        }

        var project = await db.ApiProjects
            .Include(p => p.SpecVersions)
            .Include(p => p.Targets)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OrganizationId == organizationId, ct);

        if (project is null)
        {
            return ApiResults.NotFound("The project");
        }

        // Buffer once: the bytes are needed for hashing, validation and storage.
        using var buffer = new MemoryStream();
        await using (var upload = file.OpenReadStream())
        {
            await upload.CopyToAsync(buffer, ct);
        }

        buffer.Position = 0;
        var contentHash = Convert.ToHexString(await SHA256.HashDataAsync(buffer, ct)).ToLowerInvariant();

        // Refused, not silently accepted. This used to return 200 with the existing version
        // and no diagnostics, which the UI reported as a successful upload of a version it had
        // never created — the upload looked broken precisely because it had done nothing.
        var duplicate = project.SpecVersions.FirstOrDefault(s => s.ContentHash == contentHash);
        if (duplicate is not null)
        {
            var detail =
                $"This document is byte-for-byte spec #{duplicate.Sequence}, uploaded on "
                + $"{duplicate.CreatedAt:yyyy-MM-dd}. Nothing about the contract has changed.";

            // Whether that version can be removed is the operator's next question, so answer
            // it here rather than after a second failed attempt.
            if (await deleter.PublishedFromAsync(duplicate.Id, ct) is { } published)
            {
                detail += $" {published.Reason}";
            }

            return ApiResults.Conflict(detail);
        }

        buffer.Position = 0;
        var previousApiVersion = project.SpecVersions
            .OrderByDescending(s => s.Sequence)
            .Select(s => s.ApiVersion)
            .FirstOrDefault();

        var validation = await validator.ValidateAsync(
            buffer, file.FileName, project.StrictValidation, previousApiVersion, ct);

        var specVersion = project.AddSpecVersion(
            storageKeyFactory: id =>
                StorageKeys.Spec(organizationId, projectId, id, file.FileName, extension),
            contentHash: contentHash,
            sizeInBytes: file.Length,
            source: SpecSource.Upload);

        buffer.Position = 0;
        await storage.PutAsync(
            specVersion.StorageKey,
            buffer,
            extension == ".json" ? "application/json" : "application/yaml",
            ct);

        specVersion.MarkValidated(
            status: validation.Status,
            apiTitle: validation.ApiTitle,
            apiVersion: validation.ApiVersion,
            openApiVersion: validation.OpenApiVersion,
            informationCount: validation.InformationCount,
            warningCount: validation.WarningCount,
            errorCount: validation.ErrorCount,
            diagnosticsJson: System.Text.Json.JsonSerializer.Serialize(validation.Diagnostics));

        // Auto-publish targets get a run immediately — this is the "zero friction from
        // commit to publish" path. Everything else waits for an explicit trigger.
        var triggered = new List<Guid>();
        if (specVersion.IsGeneratable)
        {
            foreach (var target in project.Targets.Where(t => t.AutoPublish && t.Language.IsSupported()))
            {
                var run = new PipelineRun(
                    organizationId,
                    projectId,
                    specVersion.Id,
                    target.Id,
                    RunTrigger.SpecUpload,
                    publishRequested: true,
                    principal.Id());

                db.PipelineRuns.Add(run);
                triggered.Add(run.Id);
            }
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/organizations/{organizationId}/projects/{projectId}/specs",
            new SpecUploadResponse(ToResponse(specVersion), validation.Diagnostics, triggered));
    }

    private static async Task<IResult> DeleteAsync(
        Guid organizationId,
        Guid projectId,
        Guid specVersionId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        ResourceDeleter deleter,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Member, ct))
        {
            return ApiResults.NotFoundOrForbidden();
        }

        var exists = await db.SpecVersions
            .AsNoTracking()
            .AnyAsync(
                s => s.Id == specVersionId
                     && s.ApiProjectId == projectId
                     && db.ApiProjects.Any(p => p.Id == projectId && p.OrganizationId == organizationId),
                ct);

        if (!exists)
        {
            return ApiResults.NotFound("The specification version");
        }

        if (await deleter.DeleteSpecVersionAsync(specVersionId, ct) is { } refusal)
        {
            return ApiResults.Conflict(refusal.Reason);
        }

        return TypedResults.NoContent();
    }

    private static SpecVersionResponse ToResponse(SpecVersion s) =>
        new(s.Id,
            s.Sequence,
            s.ValidationStatus,
            s.ApiTitle,
            s.ApiVersion,
            s.OpenApiVersion,
            StorageKeys.FormatOf(s.StorageKey),
            s.InformationCount,
            s.WarningCount,
            s.ErrorCount,
            s.SizeInBytes,
            s.Source,
            s.CreatedAt);
}
