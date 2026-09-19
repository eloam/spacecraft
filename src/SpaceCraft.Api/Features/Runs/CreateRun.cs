using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Runs.Responses;
using SpaceCraft.Api.Features.Runs.Requests;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Runs;

public static class CreateRun
{
    public static async Task<Results<Created<RunResponse>, ProblemHttpResult>> HandleAsync(
        Guid organizationId,
        [FromBody] CreateRunRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        // Publishing is irreversible on a public registry, so it needs a higher role than
        // simply generating and building.
        var required = request.Publish ? OrganizationRole.Admin : OrganizationRole.Member;
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), required, ct) is { } denied)
        {
            return denied;
        }

        var spec = await db.SpecVersions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SpecVersionId, ct);

        if (spec is null)
        {
            return ApiResults.NotFound("The spec version");
        }

        var target = await db.SdkTargets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.SdkTargetId, ct);

        if (target is null || target.ApiProjectId != spec.ApiProjectId)
        {
            return ApiResults.NotFound("The SDK target");
        }

        var project = await db.ApiProjects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == spec.ApiProjectId && p.OrganizationId == organizationId, ct);

        if (project is null)
        {
            return ApiResults.NotFoundOrForbidden();
        }

        if (!spec.IsGeneratable)
        {
            return ApiResults.Conflict("This spec version failed validation and cannot be generated from.");
        }

        // A version the target's registry cannot take is deliberately not refused here. The
        // pipeline's first step is validation, and judging the document against the registry
        // it is bound for is what that step is for. Refusing at creation would be quieter in
        // the wrong way: no run, no step, no log, and nothing in the history for someone
        // waiting on a publish to find. A run that fails at validate says the same sentence
        // and leaves it written down.

        // Refuse to queue a second run for the same spec and target while one is in flight.
        var alreadyQueued = await db.PipelineRuns.AnyAsync(
            r => r.SpecVersionId == spec.Id
                 && r.SdkTargetId == target.Id
                 && (r.Status == RunStatus.Queued || r.Status == RunStatus.Running),
            ct);

        if (alreadyQueued)
        {
            return ApiResults.Conflict("A run for this spec version and target is already in progress.");
        }

        // Read now, stored on the run: the address travels with the credential, so revoking
        // or repointing one later must not change where this run says it published.
        var credential = target.RegistryCredentialId is { } credentialId
            ? await db.RegistryCredentials.AsNoTracking()
                .Where(c => c.Id == credentialId && c.OrganizationId == organizationId)
                .Select(c => new { c.RegistryUrl, c.Label })
                .FirstOrDefaultAsync(ct)
            : null;

        var run = new PipelineRun(
            organizationId,
            project.Id,
            spec.Id,
            spec.Sequence,
            target,
            credential?.RegistryUrl,
            credential?.Label,
            RunTrigger.Manual,
            request.Publish,
            principal.Id());

        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/organizations/{organizationId}/runs/{run.Id}", RunResponse.From(run));
    }
}
