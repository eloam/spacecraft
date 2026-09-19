using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Projects.Responses;
using SpaceCraft.Api.Features.Projects.Requests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Projects;

public static class AddTarget
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid projectId,
        [FromBody] AddTargetRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        var project = await db.ApiProjects
            .Include(p => p.Targets)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OrganizationId == organizationId, ct);

        if (project is null)
        {
            return ApiResults.NotFound("The project");
        }

        // The language, its registry and the package name's shape are the request's
        // Validator's business, so a registry the caller named is already the language's own.
        var registry = request.Registry ?? request.Language.DefaultRegistry();

        // The uniqueness the database used to enforce across every tenant, narrowed to the
        // organization it actually belongs in. A registry decides who owns a name and says so
        // at publish time; what is worth catching here is one organization pointing two
        // targets at the same package, which is a mistake rather than a conflict.
        var taken = await db.SdkTargets
            .AsNoTracking()
            .Where(t => t.Registry == registry && t.PackageName == request.PackageName.Trim())
            .Where(t => db.ApiProjects.Any(p => p.Id == t.ApiProjectId && p.OrganizationId == organizationId))
            .AnyAsync(ct);

        if (taken)
        {
            return ApiResults.Conflict(
                $"Another project in this organization already publishes {request.PackageName.Trim()} "
                + $"to {registry}.");
        }

        if (await TargetCredential.RejectUnusableAsync(
                organizationId, registry, request.RegistryCredentialId, db, ct) is { } refusal)
        {
            return refusal;
        }

        SdkTarget target;
        try
        {
            target = project.AddTarget(request.Language, request.PackageName?.Trim() ?? string.Empty, registry);
        }
        catch (ArgumentException ex)
        {
            // A backstop: the Validator has already judged the package name against this registry.
            return ApiResults.Invalid(nameof(request.PackageName), ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResults.Conflict(ex.Message);
        }

        target.SetAutoPublish(request.AutoPublish);
        target.UseCredential(request.RegistryCredentialId);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Trips the (registry, package_name) unique index: another tenant already claimed it.
            return ApiResults.Conflict(
                $"'{request.PackageName}' is already registered on {registry} by another project.");
        }

        return TypedResults.Created(
            $"/api/v1/organizations/{organizationId}/projects/{projectId}",
            TargetResponse.From(target));
    }
}
