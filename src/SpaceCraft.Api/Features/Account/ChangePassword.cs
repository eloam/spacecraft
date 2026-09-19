using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Api.Features.Auth;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Account.Requests;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Account;

public static class ChangePassword
{
    public static async Task<IResult> HandleAsync(
        [FromBody] ChangePasswordRequest request,
        ClaimsPrincipal principal,
        UserManager<SpaceCraftUser> userManager,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(principal.Id().ToString());
        if (user is null)
        {
            return ApiResults.NotFound("The user");
        }

        // The current password is required even though the caller is authenticated: an access
        // token left behind on a shared machine should not be enough to take the account over.
        var result = await userManager.ChangePasswordAsync(
            user, request.CurrentPassword ?? string.Empty, request.NewPassword ?? string.Empty);

        if (!result.Succeeded)
        {
            var wrongCurrent = result.Errors.Any(e => e.Code == "PasswordMismatch");

            return ApiResults.Invalid(
                wrongCurrent ? nameof(request.CurrentPassword) : nameof(request.NewPassword),
                wrongCurrent
                    ? "That is not your current password."
                    : string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        // Same reasoning as a reset: a password change is the moment to end sessions the
        // caller may no longer control. Their own session survives so they are not signed out
        // of the page they are standing on.
        await Sessions.RevokeAsync(db, user.Id, except: principal.SessionId(), ct);

        // And any reset link still outstanding, for the same reason and a sharper one. This is
        // the action the product tells someone to take when they think their account is
        // reachable by somebody else — and it left the one thing that grants a new password
        // without knowing the old one still redeemable for up to an hour.
        await PasswordResets.InvalidateAsync(db, user.Id, DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }
}
