namespace SpaceCraft.Api.Features.Projects;

public static class ProjectEndpoints
{
    public static RouteGroupBuilder MapProjectEndpoints(this RouteGroupBuilder api)
    {
        // The guard's 404 to a non-member, once for the group: every route below opens with
        // it. Its 403 is declared by the routes that ask for more than Viewer, since a read a
        // Viewer may make cannot answer it.
        var group = api.MapGroup("/organizations/{organizationId:guid}/projects")
            .WithTags("Projects")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListProjects.HandleAsync)
            .WithSummary("Lists the organization's API projects.");

        group.MapPost("/", CreateProject.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Creates an API project.");

        group.MapGet("/{projectId:guid}", GetProject.HandleAsync)
            .WithSummary("Returns one project with its SDK targets.");

        group.MapPost("/{projectId:guid}/targets", AddTarget.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Adds an SDK target to a project.");

        group.MapPut("/{projectId:guid}/strict-validation", SetStrictValidation.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Turns the SDK-readiness checks on or off for this project.");

        group.MapPatch("/{projectId:guid}", RenameProject.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            // Two conflicts: the new slug is a sibling project's, or the organization row
            // moved under the rename and the database refused the write.
            .ProducesProblem(StatusCodes.Status409Conflict)
            // The slug moves with the name — ApiProject.Rename recomputes it. The summary
            // promised the opposite, which was the same false invariant the slug cache rested
            // on until it was fixed.
            .WithSummary("Renames a project, and its slug with it. Links to the old address stop resolving.");

        group.MapDelete("/{projectId:guid}", DeleteProject.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Deletes a project with its specs, targets and run history.");

        group.MapPatch("/{projectId:guid}/targets/{targetId:guid}", UpdateTarget.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Changes an SDK target's package name or auto-publish setting.");

        group.MapDelete("/{projectId:guid}/targets/{targetId:guid}", DeleteTarget.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Deletes an SDK target and the runs that produced it.");

        return api;
    }
}
