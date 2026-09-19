namespace SpaceCraft.Api.Features.Specs;

public static class SpecEndpoints
{
    public static RouteGroupBuilder MapSpecEndpoints(this RouteGroupBuilder api)
    {
        // The guard's 404 to a non-member, once for the group: every route below opens with
        // it. Its 403 is declared by the routes that ask for more than Viewer, since a read a
        // Viewer may make cannot answer it.
        var group = api.MapGroup("/organizations/{organizationId:guid}/projects/{projectId:guid}/specs")
            .WithTags("Specs")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListSpecs.HandleAsync)
            .WithSummary("Lists the project's spec versions, newest first.");

        group.MapGet("/{specVersionId:guid}/diagnostics", GetSpecDiagnostics.HandleAsync)
            .WithSummary("Returns the validation findings recorded when the spec was uploaded.");

        group.MapGet("/{specVersionId:guid}/content", GetSpecContent.HandleAsync)
            .WithSummary("Returns the document as uploaded, for showing findings in context.");

        group.MapPost("/{specVersionId:guid}/revalidate", RevalidateSpec.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary(
                "Re-runs validation over a stored document and replaces its findings. "
                + "Runs in the project's mode unless the body overrides it.");

        group.MapDelete("/{specVersionId:guid}", DeleteSpec.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Deletes a spec version, unless a package was published from it.");

        group.MapPost("/", UploadSpec.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            // Identical bytes are refused: nothing would change, and the old response looked
            // like a successful upload of a version it had not created.
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Uploads an OpenAPI document as a new spec version.")
            // These two return IEndpointConventionBuilder rather than RouteHandlerBuilder,
            // which ends the typed chain — so they come last.
            .WithRequestTimeout(TimeSpan.FromMinutes(2))
            .DisableAntiforgery();

        return api;
    }
}
