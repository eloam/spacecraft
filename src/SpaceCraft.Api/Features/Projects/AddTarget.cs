using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Projects.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Projects;

public sealed record AddTargetRequest(
    SdkLanguage Language,
    string PackageName,
    RegistryKind? Registry,
    bool AutoPublish,
    /// <summary>Which credential publishes it. Null while the organization has only one.</summary>
    Guid? RegistryCredentialId = null);

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

        // The domain models six languages; only the ones with a working generator, builder
        // and publisher are accepted, so a user cannot queue a run that can never succeed.
        if (!request.Language.IsSupported())
        {
            return ApiResults.Invalid(
                nameof(request.Language),
                $"{request.Language} is not available yet. TypeScript is currently supported.");
        }

        var project = await db.ApiProjects
            .Include(p => p.Targets)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OrganizationId == organizationId, ct);

        if (project is null)
        {
            return ApiResults.NotFound("The project");
        }

        var registry = request.Registry ?? request.Language.DefaultRegistry();

        // A caller may name the registry, and nothing checked it belonged to the language.
        // A Java target with Registry=Npm resolved both seams successfully — the Java
        // generator built a .jar and the npm publisher was handed it — so the pairing is
        // refused here rather than discovered at the last step of a run.
        if (registry != request.Language.DefaultRegistry())
        {
            return ApiResults.Invalid(
                nameof(request.Registry),
                $"{request.Language} publishes to {request.Language.DefaultRegistry()}, not to "
                + $"{registry}. Point the credential at your own {request.Language.DefaultRegistry()} "
                + "server if it is not the public one.");
        }

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

        if (request.AutoPublish && request.RegistryCredentialId is null)
        {
            return ApiResults.Invalid(
                "autoPublish",
                "A target that publishes nowhere cannot publish automatically. Choose a "
                + "credential, or leave automatic publishing off.");
        }

        SdkTarget target;
        try
        {
            target = project.AddTarget(request.Language, request.PackageName?.Trim() ?? string.Empty, registry);
        }
        catch (ArgumentException ex)
        {
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
