using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Members;

public static class RevokeInvitation
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid invitationId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        // Scoped to the organization that was authorised, not merely to the id: an invitation
        // id is a guid somebody could have been told.
        var invitation = await db.OrganizationInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.OrganizationId == organizationId, ct);

        if (invitation is null)
        {
            return ApiResults.NotFound("The invitation");
        }

        invitation.Revoke(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }
}
