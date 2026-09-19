using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Auth.Responses;
using SpaceCraft.Api.Features.Auth.Requests;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Api.Features.Auth;

public static class Login
{
    public static async Task<IResult> HandleAsync(
        [FromBody] LoginRequest request,
        UserManager<SpaceCraftUser> userManager,
        SpaceCraftDbContext db,
        ITokenFactory tokenFactory,
        IOptions<JwtOptions> jwtOptions,
        CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(request.Email ?? string.Empty);

        // Checked before the password, not after. Asked the other way round, a locked account
        // still had every guess evaluated, so the lockout delayed nothing it was meant to.
        if (user is not null && await userManager.IsLockedOutAsync(user))
        {
            return ApiResults.Problem(
                "Account locked",
                "Too many failed attempts. Try again later.",
                StatusCodes.Status423Locked);
        }

        // Same response whether the account is missing or the password is wrong, so the
        // endpoint cannot be used to enumerate registered addresses.
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password ?? string.Empty))
        {
            // Counted here because nothing else does. CheckPasswordAsync does not touch
            // AccessFailedCount — only SignInManager.CheckPasswordSignInAsync does, and this
            // application registers Identity's core without one. So the ten-attempt lockout
            // configured in DependencyInjection was never armed, LockoutEnd was never set,
            // and the branch above could not be reached: a comment two files away claimed
            // "Identity's per-account lockout stops brute force against one account" while
            // nothing counted a single failure.
            if (user is not null)
            {
                await userManager.AccessFailedAsync(user);
            }

            return ApiResults.Problem(
                "Invalid credentials",
                "The email address or password is incorrect.",
                StatusCodes.Status401Unauthorized);
        }

        // A password that works clears the tally, or ten failures spread over months would
        // lock an account whose owner never mistyped anything twice in a row.
        if (await userManager.GetAccessFailedCountAsync(user) > 0)
        {
            await userManager.ResetAccessFailedCountAsync(user);
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        var pair = await Sessions.IssueAsync(user, db, tokenFactory, familyId: null, ct);
        return TypedResults.Ok(AuthResponse.From(pair, user));
    }
}
