using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Organizations.Responses;
using SpaceCraft.Api.Features.Organizations.Requests;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Organizations;

public static class RenameOrganization
{
    public static async Task<Results<Ok<OrganizationResponse>, ProblemHttpResult, ValidationProblem>> HandleAsync(
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

        // Well-formed, and so non-null: the shape is the request's Validator's business.
        var name = request.Name?.Trim();

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
}
