namespace SpaceCraft.Api.Features.Publications;

public static class PublicationEndpoints
{
    public static RouteGroupBuilder MapPublicationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations/{organizationId:guid}/publications")
            .WithTags("Publications")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/", ListPublications.HandleAsync)
            .Produces<List<PublicationResponse>>()
            .WithSummary("Lists SDK versions pushed to registries, newest first.");

        return api;
    }
}
