namespace SpaceCraft.Api.Features.Publications;

public static class PublicationEndpoints
{
    public static RouteGroupBuilder MapPublicationEndpoints(this RouteGroupBuilder api)
    {
        // The guard's 404 to a non-member, once for the group. Its 403 is not declared: the
        // one route asks for Viewer, the lowest role, so a member is never refused.
        var group = api.MapGroup("/organizations/{organizationId:guid}/publications")
            .WithTags("Publications")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListPublications.HandleAsync)
            .WithSummary("Lists SDK versions pushed to registries, newest first.");

        return api;
    }
}
