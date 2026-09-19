using SpaceCraft.Api.Extensions;
using SpaceCraft.Api.Features.Auth.Responses;

namespace SpaceCraft.Api.Features.Auth;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Auth");

        // Response types are declared explicitly so they reach the OpenAPI document.
        // Minimal API handlers returning IResult are opaque to the schema generator, and an
        // API whose own contract is under-specified is a poor advertisement for a product
        // that generates SDKs from OpenAPI.
        group.MapPost("/register", Register.HandleAsync)
            .AllowAnonymous()
            .Produces<AuthResponse>()
            .ProducesValidationProblem()
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Creates an account and returns a token pair.");

        group.MapPost("/login", Login.HandleAsync)
            .AllowAnonymous()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Exchanges credentials for a token pair.");

        group.MapPost("/refresh", RefreshSession.HandleAsync)
            .AllowAnonymous()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem()
            .WithSummary("Rotates a refresh token for a new token pair.");

        group.MapPost("/forgot-password", ForgotPassword.Handle)
            .AllowAnonymous()
            .Produces<AcknowledgementResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Emails a password reset link, if the address has an account.");

        group.MapPost("/reset-password", ResetPassword.HandleAsync)
            .AllowAnonymous()
            .Produces<AcknowledgementResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesValidationProblem()
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Sets a new password using an emailed reset token.");

        group.MapPost("/logout", Logout.HandleAsync)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .WithSummary("Revokes the caller's refresh token family.");

        group.MapGet("/me", GetCurrentUser.HandleAsync)
            .RequireAuthorization()
            .Produces<UserResponse>()
            .WithSummary("Returns the authenticated user.");

        return api;
    }
}
