using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Auth.Responses;
using SpaceCraft.Api.Features.Auth.Requests;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Auth;

public static class ResetPassword
{
    public static async Task<Results<Ok<AcknowledgementResponse>, ProblemHttpResult, ValidationProblem>> HandleAsync(
        [FromBody] ResetPasswordRequest request,
        UserManager<SpaceCraftUser> userManager,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        // That a token and a new password are present is the request's Validator's business.
        //
        // One message for every way a token can be unusable — unknown, expired, already
        // spent, superseded. Distinguishing them tells an attacker which guesses were close.
        var rejected = ApiResults.Problem(
            "Invalid reset link",
            "This reset link is no longer valid. Request a new one.",
            StatusCodes.Status400BadRequest);

        var now = DateTimeOffset.UtcNow;
        var hash = OpaqueTokens.Hash(request.Token);

        var stored = await db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null || !stored.IsRedeemable(now))
        {
            return rejected;
        }

        var user = await userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null)
        {
            return rejected;
        }

        // Identity owns the password itself: going through it applies the configured password
        // policy and rolls the security stamp. Our token governs authorisation, not the change.
        var identityToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, identityToken, request.NewPassword);

        if (!result.Succeeded)
        {
            // A policy failure is the caller's own input, so it is safe — and necessary — to
            // say what was wrong. The token stays unspent so they can try again.
            return ApiResults.Invalid(
                nameof(request.NewPassword),
                string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        stored.MarkUsed(now);

        // Whoever knew the old password may still hold a refresh token. Resetting is exactly
        // the moment to end every existing session rather than leave one open — every one,
        // since nobody is signed in on the page that posts a reset. The save inside RevokeAsync
        // carries the used mark with it.
        await Sessions.RevokeAsync(db, user.Id, except: null, ct);

        return TypedResults.Ok(
            new AcknowledgementResponse("Your password has been changed. Sign in with it now."));
    }
}
