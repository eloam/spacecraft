using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Api.Features.Auth.Responses;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace SpaceCraft.Api.Features.Auth;

public static class GetCurrentUser
{
    public static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> HandleAsync(
        ClaimsPrincipal principal,
        UserManager<SpaceCraftUser> userManager,
        CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(principal.Id().ToString());
        if (user is null)
        {
            return ApiResults.NotFound("The user");
        }

        return TypedResults.Ok(new UserResponse(user.Id, user.Email ?? string.Empty, user.DisplayName));
    }
}
