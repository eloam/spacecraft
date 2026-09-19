using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Account.Responses;
using SpaceCraft.Api.Features.Account.Requests;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Account;

public static class UpdateProfile
{
    public static async Task<Results<Ok<AccountResponse>, ProblemHttpResult, ValidationProblem>> HandleAsync(
        [FromBody] UpdateProfileRequest request,
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

        // The length is the request's Validator's business. Empty clears the name rather than
        // storing whitespace.
        var name = request.DisplayName?.Trim();
        user.DisplayName = string.IsNullOrWhiteSpace(name) ? null : name;

        // Identity reports a user row changed under it — a password change landing at the
        // same instant — as a failed result rather than an exception, so an unchecked result
        // answered 200 with a name that was never stored. UpdateAsync also fails for a user
        // that no longer passes its validators (user name, unique email), which a change to
        // the display name alone cannot cause.
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return ApiResults.Conflict("Someone else changed this account at the same time. Reload and try again.");
        }

        var organizations = await db.OrganizationMembers.CountAsync(m => m.UserId == user.Id, ct);

        return TypedResults.Ok(new AccountResponse(
            user.Id, user.Email ?? string.Empty, user.DisplayName,
            user.CreatedAt, user.LastLoginAt, organizations));
    }
}
