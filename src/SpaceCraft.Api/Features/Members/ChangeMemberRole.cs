using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Members;

public sealed record ChangeRoleRequest(OrganizationRole Role);

public static class ChangeMemberRole
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid userId,
        [FromBody] ChangeRoleRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        var caller = principal.Id();

        if (await access.DenyUnlessAsync(organizationId, caller, OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        var organization = await db.Organizations
            .Include(o => o.Members)
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        if (organization is null || organization.Members.All(m => m.UserId != userId))
        {
            return ApiResults.NotFound("The member");
        }

        // Nobody grants a role they do not hold, and nobody grants one to themselves.
        //
        // Admin was enough to reach here, and Admin could therefore write Owner onto its own
        // membership — then demote the real owner and delete the organization, the one action
        // deletion deliberately reserves because "an admin was added by someone else". The
        // same rule protects the other direction: an Admin must not demote an Owner, or the
        // reservation is worked around from below instead of above.
        var callerRole = await access.GetRoleAsync(organizationId, caller, ct);
        var currentRole = organization.Members.Single(m => m.UserId == userId).Role;

        if (userId == caller && request.Role != currentRole)
        {
            return ApiResults.Problem(
                "Forbidden",
                "You cannot change your own role. Ask another owner or admin.",
                StatusCodes.Status403Forbidden);
        }

        if (callerRole is not { } held || request.Role > held)
        {
            return ApiResults.Problem(
                "Forbidden",
                $"Granting the {request.Role} role takes that role or higher.",
                StatusCodes.Status403Forbidden);
        }

        if (currentRole > held)
        {
            return ApiResults.Problem(
                "Forbidden",
                $"Changing the role of an {currentRole} takes that role or higher.",
                StatusCodes.Status403Forbidden);
        }

        try
        {
            organization.ChangeMemberRole(userId, request.Role);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResults.Conflict(ex.Message);
        }

        await db.SaveChangesAsync(ct);

        var members = await MemberQueries.ListAsync(db, organizationId, caller, ct);
        return TypedResults.Ok(members.First(m => m.UserId == userId));
    }
}
