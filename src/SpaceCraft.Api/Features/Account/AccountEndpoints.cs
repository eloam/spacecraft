using SpaceCraft.Api.Extensions;

namespace SpaceCraft.Api.Features.Account;

public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/account").WithTags("Account").RequireAuthorization();

        group.MapGet("/", GetAccount.HandleAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Returns the signed-in user's account.");

        group.MapPatch("/", UpdateProfile.HandleAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Updates the display name.");

        group.MapPost("/password", ChangePassword.HandleAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Changes the password, ending every other session.");

        group.MapGet("/sessions", ListSessions.HandleAsync)
            .WithSummary("Lists active sessions.");

        group.MapDelete("/sessions/{sessionId:guid}", RevokeSession.HandleAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Signs out one session.");

        group.MapDelete("/sessions", RevokeOtherSessions.HandleAsync)
            .WithSummary("Signs out every session except this one.");

        group.MapGet("/deletion-blockers", GetDeletionBlockers.HandleAsync)
            .WithSummary("Lists what currently prevents deleting the account.");

        group.MapDelete("/", DeleteAccount.HandleAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Permanently deletes the account.");

        return api;
    }
}
