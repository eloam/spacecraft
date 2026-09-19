using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Members;

public static class ListInvitations
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        // Admin, like sending one: an outstanding invitation names an address, and who has
        // been asked to join is not general knowledge inside an organization.
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        var now = DateTimeOffset.UtcNow;

        // The shared predicate, not a copy of it. This list once carried its own and was not
        // told when declining was added, so an invitation somebody had already refused went on
        // being offered as pending — with a link that no longer worked.
        var open = await db.OrganizationInvitations
            .AsNoTracking()
            .Where(i => i.OrganizationId == organizationId)
            .Where(OrganizationInvitation.Usable(now))
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

        return TypedResults.Ok(open.Select(InvitationResponse.From).ToList());
    }
}
