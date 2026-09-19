using System.Security.Claims;
using System.Security.Cryptography;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Infrastructure.OpenApi;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Specs;

public static class UploadSpec
{
    /// <summary>
    /// Matches <see cref="SpecLimits.MaxSizeInBytes"/>. Enforced by the server before the
    /// body is buffered, so an oversized upload is rejected without being read into memory.
    /// </summary>
    private const long MaxUploadBytes = SpecLimits.MaxSizeInBytes;

    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid projectId,
        IFormFile file,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        ISpecValidator validator,
        IObjectStorage storage,
        SpecDeleter deleter,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Member, ct) is { } denied)
        {
            return denied;
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
            // One lookup for the whole loop rather than one per target: several targets in a
            // project commonly share a credential, and this runs on the upload path.
            var credentialUrls = await db.RegistryCredentials.AsNoTracking()
                .Where(c => c.OrganizationId == organizationId)
                .ToDictionaryAsync(c => c.Id, c => new { c.RegistryUrl, c.Label }, ct);

            foreach (var target in project.Targets.Where(t => t.AutoPublish && t.Language.IsSupported()))
            {
                // Targets whose registry cannot take this version are queued like any other,
                // and fail at validation. Skipping them was quieter and worse: an automatic
                // target simply produced nothing — no run, no failure, no explanation — and
                // the person who pushed the commit had only the spec's own warning to find,
                // in a place they had no reason to open.

                var run = new PipelineRun(
                    organizationId,
                    projectId,
                    specVersion.Id,
                    specVersion.Sequence,
                    target,
                    target.RegistryCredentialId is { } id && credentialUrls.TryGetValue(id, out var cred)
                        ? cred.RegistryUrl
                        : null,
                    target.RegistryCredentialId is { } id2 && credentialUrls.TryGetValue(id2, out var cred2)
                        ? cred2.Label
                        : null,
                    RunTrigger.SpecUpload,
                    publishRequested: true,
                    principal.Id());

                db.PipelineRuns.Add(run);
                triggered.Add(run.Id);
            }
        }

        DbUpdateException? conflict = null;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Kept rather than swallowed, and narrowed below: telling a race apart from any
            // other write failure needs a query, and a catch filter cannot await one.
            conflict = ex;
        }

        if (conflict is not null)
        {
            // Two uploads to one project at the same moment. The sequence is the count of
            // what this project already holds, computed in memory, so both arrivals claimed
            // the same number and the unique index refused the second — as a 500, after its
            // document was already in the bucket, leaving an object nothing points at.
            //
            // Answered rather than retried: the caller knows to send it again, and a retry
            // here would have to re-run validation and the auto-publish triggers against a
            // project that has moved on in the meantime.
            if (!await SequenceTakenAsync(db, projectId, specVersion.Sequence, ct))
            {
                // Not the race. Rethrown as it was, so whatever it really is reaches the
                // handler that logs it rather than being renamed on the way.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(conflict).Throw();
            }

            await storage.DeleteAsync(specVersion.StorageKey, ct);

            return ApiResults.Conflict(
                "Another document was uploaded to this project at the same moment. "
                + "Send this one again.");
        }

        return TypedResults.Created(
            $"/api/v1/organizations/{organizationId}/projects/{projectId}/specs",
            new SpecUploadResponse(
                SpecVersionResponse.From(specVersion), validation.Diagnostics, triggered, project.StrictValidation));
    }

    /// <summary>
    /// True when the number this upload claimed now belongs to a different document — which
    /// is what tells a racing upload apart from any other write failure.
    /// </summary>
    private static async Task<bool> SequenceTakenAsync(
        SpaceCraftDbContext db, Guid projectId, int sequence, CancellationToken ct)
    {
        // A fresh context: the failed SaveChanges left this one holding entries it could not
        // write, and asking it anything about the database would replay them.
        return await db.SpecVersions
            .AsNoTracking()
            .AnyAsync(s => s.ApiProjectId == projectId && s.Sequence == sequence, ct);
    }
}
