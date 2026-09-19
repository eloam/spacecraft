using SpaceCraft.Api.Extensions;
using SpaceCraft.Api.Features.Account.Responses;

namespace SpaceCraft.Api.Features.Account;

public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/account").WithTags("Account").RequireAuthorization();

        group.MapGet("/", GetAccount.HandleAsync)
            .Produces<AccountResponse>()
            .WithSummary("Returns the signed-in user's account.");

        group.MapPatch("/", UpdateProfile.HandleAsync)
            .Produces<AccountResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem()
            .WithSummary("Updates the display name.");

        group.MapPost("/password", ChangePassword.HandleAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Changes the password, ending every other session.");

        group.MapGet("/sessions", ListSessions.HandleAsync)
            .Produces<IReadOnlyList<SessionResponse>>()
            .WithSummary("Lists active sessions.");

        group.MapDelete("/sessions/{sessionId:guid}", RevokeSession.HandleAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Signs out one session.");

        group.MapDelete("/sessions", RevokeOtherSessions.HandleAsync)
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Signs out every session except this one.");

        group.MapGet("/deletion-blockers", GetDeletionBlockers.HandleAsync)
            .Produces<IReadOnlyList<DeletionBlocker>>()
            .WithSummary("Lists what currently prevents deleting the account.");

        group.MapDelete("/", DeleteAccount.HandleAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem()
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Permanently deletes the account.");

        return api;
    }
}
