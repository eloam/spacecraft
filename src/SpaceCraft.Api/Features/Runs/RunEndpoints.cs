namespace SpaceCraft.Api.Features.Runs;

public static class RunEndpoints
{
    public static RouteGroupBuilder MapRunEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations/{organizationId:guid}/runs")
            .WithTags("Runs")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/", ListRuns.HandleAsync)
            .Produces<List<RunResponse>>()
            .WithSummary("Lists recent pipeline runs.");

        group.MapPost("/", CreateRun.HandleAsync)
            .Produces<RunResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Queues a pipeline run.");

        group.MapGet("/{runId:guid}", GetRun.HandleAsync)
            .Produces<RunResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns one run with its steps.");

        group.MapPost("/{runId:guid}/cancel", CancelRun.HandleAsync)
            .Produces<RunResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Cancels a queued or running pipeline run.");

        group.MapGet("/{runId:guid}/steps/{step}/log", GetStepLog.HandleAsync)
            .Produces<string>(StatusCodes.Status200OK, "text/plain")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns a step's captured log.");

        group.MapGet("/{runId:guid}/artifact", GetArtifact.HandleAsync)
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns the packaged SDK this run produced.");

        return api;
    }
}
