using SpaceCraft.Api.Extensions;

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
            .WithSummary("Registers an email for launch notifications.");

        group.MapGet("/confirm", ConfirmSignup.HandleAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Confirms a waitlist signup from the emailed link.");

        return api;
    }
}
