using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Api.Features.Deletion;
using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features;

public sealed record CreateProjectRequest(string Name, string? Slug);

public sealed record AddTargetRequest(SdkLanguage Language, string PackageName, RegistryKind? Registry, bool AutoPublish);

public sealed record TargetResponse(
    Guid Id,
    SdkLanguage Language,
    string PackageName,
    RegistryKind Registry,
    bool AutoPublish,
    string? LastPublishedVersion,
    DateTimeOffset? LastPublishedAt);

public sealed record ProjectResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Slug,
    SpecSource Source,
    string? GitHubRepository,
    int SpecVersionCount,
    IReadOnlyList<TargetResponse> Targets,
    DateTimeOffset CreatedAt);

public sealed record RenameProjectRequest(string Name);

/// <summary>
/// Typing the name back is the confirmation. Shared by project and target deletion so the
/// two read identically, and named for what it is rather than for either one.
/// </summary>
public sealed record ConfirmByNameRequest(string Name);

public static class ProjectEndpoints
{
    public static RouteGroupBuilder MapProjectEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations/{organizationId:guid}/projects")
            .WithTags("Projects")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .Produces<List<ProjectResponse>>()
            .WithSummary("Lists the organization's API projects.");

        group.MapPost("/", CreateAsync)
            .Produces<ProjectResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Creates an API project.");

        group.MapGet("/{projectId:guid}", GetAsync)
            .Produces<ProjectResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns one project with its SDK targets.");

        group.MapPost("/{projectId:guid}/targets", AddTargetAsync)
            .Produces<TargetResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Adds an SDK target to a project.");

        group.MapPatch("/{projectId:guid}", RenameAsync)
            .Produces<ProjectResponse>()
            .ProducesValidationProblem()
            .WithSummary("Renames a project. The slug is left unchanged.");

        group.MapDelete("/{projectId:guid}", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Deletes a project with its specs, targets and run history.");

        group.MapDelete("/{projectId:guid}/targets/{targetId:guid}", DeleteTargetAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Deletes an SDK target and the runs that produced it.");

        return api;
    }

    private static async Task<IResult> RenameAsync(
        Guid organizationId,
        Guid projectId,
        [FromBody] RenameProjectRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        // Member, not Admin: renaming is reversible and does not touch anything published.
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Member, ct))
        {
            return ApiResults.NotFoundOrForbidden();
        }

        var name = request.Name?.Trim();

        if (!ResourceName.IsValid(name))
        {
            return ApiResults.Invalid(nameof(request.Name), ResourceName.Rule);
        }

        var organization = await db.Organizations
            .Include(o => o.Projects)
            .ThenInclude(p => p.Targets)
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        var project = organization?.Projects.FirstOrDefault(p => p.Id == projectId);

        if (organization is null || project is null)
        {
            return ApiResults.NotFound("The project");
        }

        try
        {
            organization.RenameProject(projectId, name!);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResults.Conflict(ex.Message);
        }

        await db.SaveChangesAsync(ct);

        var specCount = await db.SpecVersions.CountAsync(s => s.ApiProjectId == projectId, ct);

        return TypedResults.Ok(ToResponse(project, specCount));
    }

    private static async Task<IResult> DeleteAsync(
        Guid organizationId,
        Guid projectId,
        [FromBody] ConfirmByNameRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        ResourceDeleter deleter,
        CancellationToken ct)
    {
        // Admin: destroying a project's history is not the same kind of act as uploading a
        // spec, which any member may do.
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct))
        {
            return ApiResults.NotFoundOrForbidden();
        }

        var project = await db.ApiProjects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OrganizationId == organizationId, ct);

        if (project is null)
        {
            return ApiResults.NotFound("The project");
        }

        // Checked server-side as well as in the dialog: a confirmation the API does not
        // enforce is decoration, and anything scripted against this endpoint skips the UI.
        if (!string.Equals(request.Name?.Trim(), project.Name, StringComparison.Ordinal))
        {
            return ApiResults.Invalid(nameof(request.Name), $"Type the project's name exactly: {project.Name}");
        }

        if (await deleter.DeleteProjectAsync(projectId, ct) is { } refusal)
        {
            return ApiResults.Conflict(refusal.Reason);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> DeleteTargetAsync(
        Guid organizationId,
        Guid projectId,
        Guid targetId,
        [FromBody] ConfirmByNameRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        ResourceDeleter deleter,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct))
        {
            return ApiResults.NotFoundOrForbidden();
        }

        var target = await db.SdkTargets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == targetId && t.ApiProjectId == projectId, ct);

        if (target is null)
        {
            return ApiResults.NotFound("The SDK target");
        }

        if (!string.Equals(request.Name?.Trim(), target.PackageName, StringComparison.Ordinal))
        {
            return ApiResults.Invalid(
                nameof(request.Name), $"Type the package name exactly: {target.PackageName}");
        }

        if (await deleter.DeleteTargetAsync(targetId, ct) is { } refusal)
        {
            return ApiResults.Conflict(refusal.Reason);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ListAsync(
        Guid organizationId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Viewer, ct))
        {
            return ApiResults.NotFoundOrForbidden();
        }

        var projects = await db.ApiProjects
            .AsNoTracking()
            .Where(p => p.OrganizationId == organizationId)
            .Include(p => p.Targets)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var counts = await db.SpecVersions
            .Where(s => db.ApiProjects.Any(p => p.Id == s.ApiProjectId && p.OrganizationId == organizationId))
            .GroupBy(s => s.ApiProjectId)
            .Select(g => new { ApiProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ApiProjectId, x => x.Count, ct);

        return TypedResults.Ok(projects
            .Select(p => ToResponse(p, counts.GetValueOrDefault(p.Id)))
            .ToList());
    }

    private static async Task<IResult> CreateAsync(
        Guid organizationId,
        [FromBody] CreateProjectRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct))
        {
            return ApiResults.NotFoundOrForbidden();
        }

        if (!ResourceName.IsValid(request.Name))
        {
            return ApiResults.Invalid(nameof(request.Name), ResourceName.Rule);
        }

        string slug;
        try
        {
            slug = string.IsNullOrWhiteSpace(request.Slug) ? Slug.From(request.Name) : request.Slug.Trim();
        }
        catch (ArgumentException)
        {
            return ApiResults.Invalid(nameof(request.Slug), "Provide a slug: the name contains no usable characters.");
        }

        if (!Slug.IsValid(slug))
        {
            return ApiResults.Invalid(
                nameof(request.Slug),
                "A slug may contain lowercase letters, digits and single hyphens only.");
        }

        // Loaded through the aggregate so the uniqueness rule lives in one place.
        var organization = await db.Organizations
            .Include(o => o.Projects)
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        if (organization is null)
        {
            return ApiResults.NotFoundOrForbidden();
        }

        ApiProject project;
        try
        {
            project = organization.AddProject(request.Name.Trim(), slug);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResults.Conflict(ex.Message);
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/organizations/{organizationId}/projects/{project.Id}",
            ToResponse(project, 0));
    }

    private static async Task<IResult> GetAsync(
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

        var project = await db.ApiProjects
            .AsNoTracking()
            .Include(p => p.Targets)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OrganizationId == organizationId, ct);

        if (project is null)
        {
            return ApiResults.NotFound("The project");
        }

        var specCount = await db.SpecVersions.CountAsync(s => s.ApiProjectId == projectId, ct);
        return TypedResults.Ok(ToResponse(project, specCount));
    }

    private static async Task<IResult> AddTargetAsync(
        Guid organizationId,
        Guid projectId,
        [FromBody] AddTargetRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct))
        {
            return ApiResults.NotFoundOrForbidden();
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
            ToTargetResponse(target));
    }

    private static ProjectResponse ToResponse(ApiProject project, int specVersionCount) =>
        new(project.Id,
            project.OrganizationId,
            project.Name,
            project.Slug,
            project.Source,
            project.GitHubRepository,
            specVersionCount,
            project.Targets.Select(ToTargetResponse).ToList(),
            project.CreatedAt);

    private static TargetResponse ToTargetResponse(SdkTarget target) =>
        new(target.Id,
            target.Language,
            target.PackageName,
            target.Registry,
            target.AutoPublish,
            target.LastPublishedVersion,
            target.LastPublishedAt);
}
