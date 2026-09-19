using SpaceCraft.Infrastructure.OpenApi;

namespace SpaceCraft.Api.Features.Specs;

public static class SpecEndpoints
{
    public static RouteGroupBuilder MapSpecEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations/{organizationId:guid}/projects/{projectId:guid}/specs")
            .WithTags("Specs")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/", ListSpecs.HandleAsync)
            .Produces<SpecListResponse>()
            .WithSummary("Lists the project's spec versions, newest first.");

        group.MapGet("/{specVersionId:guid}/diagnostics", GetSpecDiagnostics.HandleAsync)
            .Produces<List<SpecDiagnostic>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns the validation findings recorded when the spec was uploaded.");

        group.MapGet("/{specVersionId:guid}/content", GetSpecContent.HandleAsync)
            .Produces<SpecContentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns the document as uploaded, for showing findings in context.");

        group.MapPost("/{specVersionId:guid}/revalidate", RevalidateSpec.HandleAsync)
            .Produces<SpecUploadResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary(
                "Re-runs validation over a stored document and replaces its findings. "
                + "Runs in the project's mode unless the body overrides it.");

        group.MapDelete("/{specVersionId:guid}", DeleteSpec.HandleAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Deletes a spec version, unless a package was published from it.");

        group.MapPost("/", UploadSpec.HandleAsync)
            .Produces<SpecUploadResponse>(StatusCodes.Status201Created)
            // Identical bytes are refused: nothing would change, and the old response looked
            // like a successful upload of a version it had not created.
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem()
            .WithSummary("Uploads an OpenAPI document as a new spec version.")
            // These two return IEndpointConventionBuilder rather than RouteHandlerBuilder,
            // which ends the typed chain — so they come last.
            .WithRequestTimeout(TimeSpan.FromMinutes(2))
            .DisableAntiforgery();

        return api;
    }
}
