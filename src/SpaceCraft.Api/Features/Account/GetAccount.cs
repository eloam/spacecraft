using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Account.Responses;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Account;

public static class GetAccount
{
    public static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> HandleAsync(
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

        var organizations = await db.OrganizationMembers.CountAsync(m => m.UserId == user.Id, ct);

        return TypedResults.Ok(new AccountResponse(
            user.Id, user.Email ?? string.Empty, user.DisplayName,
            user.CreatedAt, user.LastLoginAt, organizations));
    }
}
