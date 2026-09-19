using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Account.Requests;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace SpaceCraft.Api.Features.Account;

public static class DeleteAccount
{
    public static async Task<IResult> HandleAsync(
        [FromBody] DeleteAccountRequest request,
        ClaimsPrincipal principal,
        UserManager<SpaceCraftUser> userManager,
        SpaceCraftDbContext db,
        AccountDeleter deleter,
        CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(principal.Id().ToString());
        if (user is null)
        {
            return ApiResults.NotFound("The user");
        }

        // Deletion is irreversible, so it is gated on the password rather than on holding a
        // token — the same reasoning as changing one, with more at stake.
        if (!await userManager.CheckPasswordAsync(user, request.Password ?? string.Empty))
        {
            return ApiResults.Invalid(nameof(request.Password), "That is not your password.");
        }

        var blockers = await AccountQueries.DeletionBlockersAsync(db, user.Id, ct);
        if (blockers.Count > 0)
        {
            // Deleting anyway would leave an organization with no one able to administer it,
            // and its projects unreachable rather than tidied up.
            return ApiResults.Conflict(
                "Hand over or delete these organizations first: "
                + string.Join(", ", blockers.Select(b => b.OrganizationName)) + ".");
        }

        if (await deleter.DeleteAsync(user.Id, ct) is { } refusal)
        {
            return ApiResults.Conflict(refusal.Reason);
        }

        return TypedResults.NoContent();
    }
}
