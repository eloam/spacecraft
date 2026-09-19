using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Account.Responses;
using SpaceCraft.Api.Features.Account.Requests;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Account;

public static class UpdateProfile
{
    public static async Task<IResult> HandleAsync(
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
        await userManager.UpdateAsync(user);

        var organizations = await db.OrganizationMembers.CountAsync(m => m.UserId == user.Id, ct);

        return TypedResults.Ok(new AccountResponse(
            user.Id, user.Email ?? string.Empty, user.DisplayName,
            user.CreatedAt, user.LastLoginAt, organizations));
    }
}
