using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Api.Features.Deletion;
using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features;

public sealed record CreateOrganizationRequest(string Name, string? Slug);

public sealed record OrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    OrganizationRole Role,
    int ProjectCount,
    DateTimeOffset CreatedAt);

public sealed record RenameRequest(string Name);

public sealed record DeleteOrganizationRequest(string Name);

public static class OrganizationEndpoints
{
    public static RouteGroupBuilder MapOrganizationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations").WithTags("Organizations").RequireAuthorization();

        group.MapGet("/", ListAsync)
            .Produces<List<OrganizationResponse>>()
            .WithSummary("Lists organizations the caller belongs to.");

        group.MapPost("/", CreateAsync)
            .Produces<OrganizationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Creates an organization owned by the caller.");

        group.MapGet("/{organizationId:guid}", GetAsync)
            .Produces<OrganizationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns one organization.");

        group.MapPatch("/{organizationId:guid}", RenameAsync)
            .Produces<OrganizationResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem()
            // The slug moves with the name — Rename recomputes it, which is why this handler
            // has to check for a collision at all. The summary used to promise the opposite.
            .WithSummary("Renames an organization, and its slug with it. Links to the old address stop resolving.");

        group.MapDelete("/{organizationId:guid}", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem()
            .WithSummary("Permanently deletes an organization and everything in it.");

        return api;
    }

    private static async Task<IResult> RenameAsync(
        Guid organizationId,
        [FromBody] RenameRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        var name = request.Name?.Trim();

        if (!ResourceName.IsValid(name))
        {
            return ApiResults.Invalid(nameof(request.Name), ResourceName.Rule);
        }

        var organization = await db.Organizations
            .Include(o => o.Members)
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        if (organization is null)
        {
            return ApiResults.NotFoundOrForbidden();
        }

        organization.Rename(name!);

        // Organization slugs are unique across the instance, so a rename can collide with
        // someone else's — including an organization the caller cannot see.
        var taken = await db.Organizations
            .AnyAsync(o => o.Id != organizationId && o.Slug == organization.Slug, ct);

        if (taken)
        {
            return ApiResults.Conflict($"Another organization already uses the address '{organization.Slug}'.");
        }

        await db.SaveChangesAsync(ct);

        var role = organization.Members.First(m => m.UserId == principal.Id()).Role;
        var projects = await db.ApiProjects.CountAsync(p => p.OrganizationId == organizationId, ct);

        return TypedResults.Ok(new OrganizationResponse(
            organization.Id, organization.Name, organization.Slug, role, projects, organization.CreatedAt));
    }

    private static async Task<IResult> DeleteAsync(
        Guid organizationId,
        [FromBody] DeleteOrganizationRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        ResourceDeleter deleter,
        CancellationToken ct)
    {
        // Owner, not Admin: this removes every project, run and credential the organization
        // has, and an admin was added by someone else.
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Owner, ct) is { } denied)
        {
            return denied;
        }

        var organization = await db.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        if (organization is null)
        {
            return ApiResults.NotFoundOrForbidden();
        }

        // Typing the name is the only friction between a stray click and losing everything.
        if (!string.Equals(request.Name?.Trim(), organization.Name, StringComparison.Ordinal))
        {
            return ApiResults.Invalid(
                nameof(request.Name), $"Type the organization's name exactly: {organization.Name}");
        }

        if (await deleter.DeleteOrganizationAsync(organizationId, ct) is { } refusal)
        {
            return ApiResults.Conflict(refusal.Reason);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        var userId = principal.Id();

        // Order before projecting: EF cannot translate a sort over a constructed record,
        // and silently falls back to throwing rather than to client evaluation.
        var organizations = await db.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Join(db.Organizations, m => m.OrganizationId, o => o.Id, (m, o) => new { m.Role, Organization = o })
            .OrderBy(x => x.Organization.Name)
            .Select(x => new OrganizationResponse(
                x.Organization.Id,
                x.Organization.Name,
                x.Organization.Slug,
                x.Role,
                db.ApiProjects.Count(p => p.OrganizationId == x.Organization.Id),
                x.Organization.CreatedAt))
            .ToListAsync(ct);

        return TypedResults.Ok(organizations);
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateOrganizationRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
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

        if (await db.Organizations.AnyAsync(o => o.Slug == slug, ct))
        {
            return ApiResults.Conflict($"The slug '{slug}' is already taken.");
        }

        var organization = new Organization(request.Name.Trim(), slug, principal.Id());
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/organizations/{organization.Id}",
            new OrganizationResponse(
                organization.Id,
                organization.Name,
                organization.Slug,
                OrganizationRole.Owner,
                0,
                organization.CreatedAt));
    }

    private static async Task<IResult> GetAsync(
        Guid organizationId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        var role = await access.GetRoleAsync(organizationId, principal.Id(), ct);
        if (role is null)
        {
            return ApiResults.NotFoundOrForbidden();
        }

        var organization = await db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        if (organization is null)
        {
            return ApiResults.NotFoundOrForbidden();
        }

        var projectCount = await db.ApiProjects.CountAsync(p => p.OrganizationId == organizationId, ct);

        return TypedResults.Ok(new OrganizationResponse(
            organization.Id,
            organization.Name,
            organization.Slug,
            role.Value,
            projectCount,
            organization.CreatedAt));
    }
}
