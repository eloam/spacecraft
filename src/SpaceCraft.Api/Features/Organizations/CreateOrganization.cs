using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Organizations.Responses;
using SpaceCraft.Api.Features.Organizations.Requests;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Organizations;

public static class CreateOrganization
{
    public static async Task<Results<Created<OrganizationResponse>, ProblemHttpResult, ValidationProblem>> HandleAsync(
        [FromBody] CreateOrganizationRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        // The shape of the name and slug is the request's Validator's business. A valid name
        // starts with a letter or digit, so Slug.From cannot throw here.
        var slug = string.IsNullOrWhiteSpace(request.Slug) ? Slug.From(request.Name) : request.Slug.Trim();

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
}
