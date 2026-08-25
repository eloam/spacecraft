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

namespace SpaceCraft.Api.Features;

public sealed record SpecVersionResponse(
    Guid Id,
    int Sequence,
    SpecValidationStatus ValidationStatus,
    string? ApiTitle,
    string? ApiVersion,
    int WarningCount,
    int ErrorCount,
    long SizeInBytes,
    SpecSource Source,
    DateTimeOffset CreatedAt);

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

    public static RouteGroupBuilder MapSpecEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations/{organizationId:guid}/projects/{projectId:guid}/specs")
            .WithTags("Specs")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .Produces<List<SpecVersionResponse>>()
            .WithSummary("Lists the project's spec versions, newest first.");

        group.MapPost("/", UploadAsync)
            .Produces<SpecUploadResponse>(StatusCodes.Status201Created)
            // A re-upload of identical bytes returns 200 with the existing version.
            .Produces<SpecUploadResponse>()
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

        var versions = await db.SpecVersions
            .AsNoTracking()
            .Where(s => s.ApiProjectId == projectId
                        && db.ApiProjects.Any(p => p.Id == projectId && p.OrganizationId == organizationId))
            .OrderByDescending(s => s.Sequence)
            .Take(100)
            .ToListAsync(ct);

        return TypedResults.Ok(versions.Select(ToResponse).ToList());
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

        // Identical bytes mean nothing changed; re-running the pipeline would republish the
        // same package for no reason.
        var duplicate = project.SpecVersions.FirstOrDefault(s => s.ContentHash == contentHash);
        if (duplicate is not null)
        {
            return TypedResults.Ok(new SpecUploadResponse(ToResponse(duplicate), [], []));
        }

        buffer.Position = 0;
        var validation = await validator.ValidateAsync(buffer, file.FileName, ct);

        var specVersion = project.AddSpecVersion(
            storageKeyFactory: id => StorageKeys.Spec(organizationId, projectId, id, extension),
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
            validation.Status,
            validation.ApiTitle,
            validation.ApiVersion,
            validation.WarningCount,
            validation.ErrorCount,
            System.Text.Json.JsonSerializer.Serialize(validation.Diagnostics));

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

    private static SpecVersionResponse ToResponse(SpecVersion s) =>
        new(s.Id,
            s.Sequence,
            s.ValidationStatus,
            s.ApiTitle,
            s.ApiVersion,
            s.WarningCount,
            s.ErrorCount,
            s.SizeInBytes,
            s.Source,
            s.CreatedAt);
}
