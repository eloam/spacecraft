namespace SpaceCraft.Api.Features.Runs;

public static class RunEndpoints
{
    public static RouteGroupBuilder MapRunEndpoints(this RouteGroupBuilder api)
    {
        // The guard's 404 to a non-member, once for the group: every route below opens with
        // it. Its 403 is declared by the routes that ask for more than Viewer, since a read a
        // Viewer may make cannot answer it.
        var group = api.MapGroup("/organizations/{organizationId:guid}/runs")
            .WithTags("Runs")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListRuns.HandleAsync)
            .WithSummary("Lists recent pipeline runs.");

        group.MapPost("/", CreateRun.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Queues a pipeline run.");

        group.MapGet("/{runId:guid}", GetRun.HandleAsync)
            .WithSummary("Returns one run with its steps.");

        group.MapPost("/{runId:guid}/cancel", CancelRun.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Cancels a queued or running pipeline run.");

        // A text or file result declares no content type of its own, so the two below say what
        // they stream — typed, because an untyped 200 loses its content type on the way to the
        // document.
        group.MapGet("/{runId:guid}/steps/{step}/log", GetStepLog.HandleAsync)
            .Produces<string>(StatusCodes.Status200OK, "text/plain")
            .WithSummary("Returns a step's captured log.");

        group.MapGet("/{runId:guid}/artifact", GetArtifact.HandleAsync)
            .Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
            .WithSummary("Returns the packaged SDK this run produced.");

        return api;
    }
}
