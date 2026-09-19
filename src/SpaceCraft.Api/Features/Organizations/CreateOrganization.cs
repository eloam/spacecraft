using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Organizations.Responses;
using SpaceCraft.Api.Features.Organizations.Requests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Organizations;

public static class CreateOrganization
{
    public static async Task<IResult> HandleAsync(
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
}
