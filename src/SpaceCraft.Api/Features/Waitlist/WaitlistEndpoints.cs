using SpaceCraft.Api.Extensions;
using SpaceCraft.Api.Features.Waitlist.Responses;

namespace SpaceCraft.Api.Features.Waitlist;

/// <summary>
/// Backs the landing page's "Notify me" form while the product is pre-launch.
/// </summary>
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
