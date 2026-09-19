using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Projects.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Projects;

/// <summary>
/// <paramref name="StrictValidation"/> is nullable so that omitting it means "whatever the
/// default is" rather than "off" — a caller that predates the option should not silently
/// create projects without it.
/// </summary>
public sealed record CreateProjectRequest(string Name, string? Slug, bool? StrictValidation);

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
