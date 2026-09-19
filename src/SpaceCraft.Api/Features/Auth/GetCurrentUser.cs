using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace SpaceCraft.Api.Features.Auth;

public static class GetCurrentUser
{
    public static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        UserManager<SpaceCraftUser> userManager,
        CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(principal.Id().ToString());
        return user is null
            ? ApiResults.NotFound("The user")
            : TypedResults.Ok(new UserResponse(user.Id, user.Email ?? string.Empty, user.DisplayName));
    }
}
