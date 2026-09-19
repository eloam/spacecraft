using SpaceCraft.Api.Extensions;

namespace SpaceCraft.Api.Features.Waitlist;

public static class WaitlistEndpoints
{
    public static RouteGroupBuilder MapWaitlistEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/waitlist").WithTags("Waitlist").AllowAnonymous();

        group.MapPost("/", JoinWaitlist.Handle)
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .Produces<WaitlistSignupResponse>()
            .ProducesValidationProblem()
            .WithSummary("Registers an email for launch notifications.");

        group.MapGet("/confirm", ConfirmSignup.HandleAsync)
            .Produces<WaitlistSignupResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Confirms a waitlist signup from the emailed link.");

        return api;
    }
}
