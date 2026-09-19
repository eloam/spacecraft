using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Projects;

/// <summary>
/// What a target can still be changed to. Language and registry are not here: they decide
/// which generator runs and where the result goes, so changing either would make the row
/// describe something that was never built.
/// </summary>
public sealed record UpdateTargetRequest(
    string PackageName,
    bool AutoPublish,
    Guid? RegistryCredentialId = null);

/// <summary>
/// Changes the package a target publishes to, and whether it does so on its own.
///
/// Refused while the target has work in flight: a run reads the package name when it
/// reaches its publish step, so a rename mid-run would push to a package nobody asked
/// for, under a version that belongs to the other one.
/// </summary>
public static class UpdateTarget
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid projectId,
        Guid targetId,
        [FromBody] UpdateTargetRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        // Admin, like adding and deleting one: it decides what gets published where.
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        var target = await db.SdkTargets
            .Where(t => t.Id == targetId && t.ApiProjectId == projectId)
            .Where(t => db.ApiProjects.Any(p => p.Id == projectId && p.OrganizationId == organizationId))
            .FirstOrDefaultAsync(ct);

        if (target is null)
        {
            return ApiResults.NotFound("The SDK target");
        }

        if (await TargetCredential.RejectUnusableAsync(
                organizationId, target.Registry, request.RegistryCredentialId, db, ct) is { } refusal)
        {
            return refusal;
        }

        if (request.AutoPublish && request.RegistryCredentialId is null)
        {
            return ApiResults.Invalid(
                "autoPublish",
                "A target that publishes nowhere cannot publish automatically. Choose a "
                + "credential, or leave automatic publishing off.");
        }

        var packageName = request.PackageName?.Trim() ?? string.Empty;

        if (!string.Equals(packageName, target.PackageName, StringComparison.Ordinal))
        {
            var active = await db.PipelineRuns.CountAsync(
                r => r.SdkTargetId == targetId
                    && (r.Status == RunStatus.Queued || r.Status == RunStatus.Running),
                ct);

            if (active > 0)
            {
                return ApiResults.Conflict(
                    $"{active} run{(active == 1 ? " is" : "s are")} still queued or running for this target. "
                    + "Wait for them to finish, or cancel them first.");
            }

            try
            {
                target.Rename(packageName);
            }
            catch (ArgumentException ex)
            {
                return ApiResults.Invalid(nameof(request.PackageName), ex.Message);
            }
        }

        target.SetAutoPublish(request.AutoPublish);
        target.UseCredential(request.RegistryCredentialId);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Trips the (registry, package_name) unique index: another project claimed it.
            return ApiResults.Conflict(
                $"'{packageName}' is already registered on {target.Registry} by another project.");
        }

        return TypedResults.Ok(TargetResponse.From(target));
    }
}
