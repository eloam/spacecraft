using System.Security.Claims;
using SpaceCraft.Api.Common;
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

        return api;
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
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ApiResults.Invalid(nameof(request.Name), "A name is required.");
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
