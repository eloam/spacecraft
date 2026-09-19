using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Projects.Responses;
using SpaceCraft.Api.Features.Projects.Requests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Projects;

public static class CreateProject
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        [FromBody] CreateProjectRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        // The shape of the name and slug is the request's Validator's business. A valid name
        // starts with a letter or digit, so Slug.From cannot throw here.
        var slug = string.IsNullOrWhiteSpace(request.Slug) ? Slug.From(request.Name) : request.Slug.Trim();

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

            if (request.StrictValidation is { } strict)
            {
                project.SetStrictValidation(strict);
            }
        }
        catch (InvalidOperationException ex)
        {
            return ApiResults.Conflict(ex.Message);
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/organizations/{organizationId}/projects/{project.Id}",
            ProjectResponse.From(project, 0));
    }
}
