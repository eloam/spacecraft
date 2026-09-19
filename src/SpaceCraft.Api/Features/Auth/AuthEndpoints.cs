using SpaceCraft.Api.Extensions;

namespace SpaceCraft.Api.Features.Auth;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Auth");

        // What a handler answers is read off its Results<...> return type: the success body,
        // a 204, a validation problem. A ProblemHttpResult carries its status at run time and
        // so declares none, which is why the problem statuses are still listed here — and
        // only those, against the handler that answers them.
        group.MapPost("/register", Register.HandleAsync)
            .AllowAnonymous()
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Creates an account and returns a token pair.");

        group.MapPost("/login", Login.HandleAsync)
            .AllowAnonymous()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status423Locked)
            // LastLoginAt is written on the Identity row, whose ConcurrencyStamp a failed
            // guess for the same account rotates underneath it; the conflict handler answers.
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Exchanges credentials for a token pair.");

        group.MapPost("/refresh", RefreshSession.HandleAsync)
            .AllowAnonymous()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithSummary("Rotates a refresh token for a new token pair.");

        group.MapPost("/forgot-password", ForgotPassword.Handle)
            .AllowAnonymous()
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Emails a password reset link, if the address has an account.");

        group.MapPost("/reset-password", ResetPassword.HandleAsync)
            .AllowAnonymous()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Sets a new password using an emailed reset token.");

        group.MapPost("/logout", Logout.HandleAsync)
            .RequireAuthorization()
            .WithSummary("Revokes the caller's refresh token family.");

        group.MapGet("/me", GetCurrentUser.HandleAsync)
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns the authenticated user.");

        return api;
    }
}
