namespace SpaceCraft.Api.Features.Organizations;

public static class OrganizationEndpoints
{
    public static RouteGroupBuilder MapOrganizationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations").WithTags("Organizations").RequireAuthorization();

        group.MapGet("/", ListOrganizations.HandleAsync)
            .WithSummary("Lists organizations the caller belongs to.");

        group.MapPost("/", CreateOrganization.HandleAsync)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Creates an organization owned by the caller.");

        group.MapGet("/{organizationId:guid}", GetOrganization.HandleAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns one organization.");

        group.MapPatch("/{organizationId:guid}", RenameOrganization.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            // Two conflicts: the new slug is another organization's, or the row moved under
            // the rename and the database refused the write.
            .ProducesProblem(StatusCodes.Status409Conflict)
            // The slug moves with the name — Rename recomputes it, which is why this handler
            // has to check for a collision at all. The summary used to promise the opposite.
            .WithSummary("Renames an organization, and its slug with it. Links to the old address stop resolving.");

        group.MapDelete("/{organizationId:guid}", DeleteOrganization.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Permanently deletes an organization and everything in it.");

        return api;
    }
}
