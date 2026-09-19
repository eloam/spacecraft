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

public static class DeleteAccount
{
    public static async Task<IResult> HandleAsync(
        [FromBody] DeleteAccountRequest request,
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

        // Through each organization rather than by deleting the membership rows, because the
        // rows alone answer to nobody. The blockers above were counted a query ago; another
        // owner leaving in between is the case the aggregate and the organization's
        // concurrency token exist for — the one refuses on what it loaded, the other refuses
        // a write over what it did not — and neither sees a row removed behind its back.
        var organizations = await db.Organizations
            .Include(o => o.Members)
            .Where(o => o.Members.Any(m => m.UserId == user.Id))
            .ToListAsync(ct);

        foreach (var organization in organizations)
        {
            try
            {
                organization.RemoveMember(user.Id);
            }
            catch (InvalidOperationException ex)
            {
                return ApiResults.Conflict($"{organization.Name}: {ex.Message}");
            }
        }

        var resets = await db.PasswordResetTokens.Where(t => t.UserId == user.Id).ToListAsync(ct);
        db.PasswordResetTokens.RemoveRange(resets);

        // Last, and its save is the one save. Either refusal above — the aggregate's, or the
        // database's over a write the token no longer covers, answered 409 to reload and try
        // again — must leave the person signed in to do so: the aggregate's writes nothing,
        // and the database's takes the revocations back along with the removals.
        await Sessions.RevokeAsync(db, user.Id, except: null, ct);
        await userManager.DeleteAsync(user);

        return TypedResults.NoContent();
    }
}
