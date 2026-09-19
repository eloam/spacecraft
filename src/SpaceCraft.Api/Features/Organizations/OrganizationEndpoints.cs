using SpaceCraft.Api.Features.Organizations.Responses;

namespace SpaceCraft.Api.Features.Organizations;

public static class OrganizationEndpoints
{
    public static RouteGroupBuilder MapOrganizationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations").WithTags("Organizations").RequireAuthorization();

        group.MapGet("/", ListOrganizations.HandleAsync)
            .Produces<List<OrganizationResponse>>()
            .WithSummary("Lists organizations the caller belongs to.");

        group.MapPost("/", CreateOrganization.HandleAsync)
            .Produces<OrganizationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Creates an organization owned by the caller.");

        group.MapGet("/{organizationId:guid}", GetOrganization.HandleAsync)
            .Produces<OrganizationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns one organization.");

        group.MapPatch("/{organizationId:guid}", RenameOrganization.HandleAsync)
            .Produces<OrganizationResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem()
            // The slug moves with the name — Rename recomputes it, which is why this handler
            // has to check for a collision at all. The summary used to promise the opposite.
            .WithSummary("Renames an organization, and its slug with it. Links to the old address stop resolving.");

        group.MapDelete("/{organizationId:guid}", DeleteOrganization.HandleAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem()
            .WithSummary("Permanently deletes an organization and everything in it.");

        return api;
    }
}
