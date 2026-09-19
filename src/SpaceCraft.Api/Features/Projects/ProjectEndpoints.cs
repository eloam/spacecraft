using SpaceCraft.Domain.Projects;
using SpaceCraft.Api.Features.Projects.Responses;

namespace SpaceCraft.Api.Features.Projects;

public static class ProjectEndpoints
{
    public static RouteGroupBuilder MapProjectEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations/{organizationId:guid}/projects")
            .WithTags("Projects")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/", ListProjects.HandleAsync)
            .Produces<List<ProjectResponse>>()
            .WithSummary("Lists the organization's API projects.");

        group.MapPost("/", CreateProject.HandleAsync)
            .Produces<ProjectResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Creates an API project.");

        group.MapGet("/{projectId:guid}", GetProject.HandleAsync)
            .Produces<ProjectResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns one project with its SDK targets.");

        group.MapPost("/{projectId:guid}/targets", AddTarget.HandleAsync)
            .Produces<TargetResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Adds an SDK target to a project.");

        group.MapPut("/{projectId:guid}/strict-validation", SetStrictValidation.HandleAsync)
            .Produces<ProjectResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Turns the SDK-readiness checks on or off for this project.");

        group.MapPatch("/{projectId:guid}", RenameProject.HandleAsync)
            .Produces<ProjectResponse>()
            .ProducesValidationProblem()
            // The slug moves with the name — ApiProject.Rename recomputes it. The summary
            // promised the opposite, which was the same false invariant the slug cache rested
            // on until it was fixed.
            .WithSummary("Renames a project, and its slug with it. Links to the old address stop resolving.");

        group.MapDelete("/{projectId:guid}", DeleteProject.HandleAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Deletes a project with its specs, targets and run history.");

        group.MapPatch("/{projectId:guid}/targets/{targetId:guid}", UpdateTarget.HandleAsync)
            .Produces<TargetResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Changes an SDK target's package name or auto-publish setting.");

        group.MapDelete("/{projectId:guid}/targets/{targetId:guid}", DeleteTarget.HandleAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Deletes an SDK target and the runs that produced it.");

        return api;
    }
}
