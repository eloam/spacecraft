using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Notifications;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Notifications;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Members;

public static class RemoveMember
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        Guid userId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        INotificationPublisher notifications,
        CancellationToken ct)
    {
        var caller = principal.Id();

        // Anyone may remove themselves; removing someone else takes Admin.
        var required = caller == userId ? OrganizationRole.Viewer : OrganizationRole.Admin;

        var membership = await access.CheckAsync(organizationId, caller, required, ct);

        if (membership.Denied is { } denied)
        {
            return denied;
        }

        var organization = await db.Organizations
            .Include(o => o.Members)
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        if (organization is null)
        {
            return ApiResults.NotFoundOrForbidden();
        }

        // Nobody removes a member who outranks them. Without this an Admin evicted the Owner
        // and became the senior member of an organization they were merely invited into —
        // the same escalation as granting oneself Owner, reached by subtraction.
        if (caller != userId)
        {
            var target = organization.Members.FirstOrDefault(m => m.UserId == userId);

            // The role is there behind a pass: the guard refuses a non-member before this line.
            if (target is not null && target.Role > membership.Role!.Value)
            {
                return ApiResults.Problem(
                    "Forbidden",
                    $"Removing an {target.Role} takes that role or higher.",
                    StatusCodes.Status403Forbidden);
            }
        }

        // Read before the removal takes it away: a notification that cannot say what someone
        // held says nothing an admin can act on.
        var heldRole = organization.Members.FirstOrDefault(m => m.UserId == userId)?.Role;

        try
        {
            organization.RemoveMember(userId);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResults.Conflict(ex.Message);
        }

        await db.SaveChangesAsync(ct);

        var left = await MemberQueries.DescribeAsync(db, userId, ct);
        var themselves = caller == userId;

        // The person it happened to, first — and only when it happened to them rather than
        // by them. Somebody who left does not need to be told they left.
        if (!themselves)
        {
            await notifications.PublishToPersonAsync(
                organizationId,
                userId,
                new NotificationContent(
                    NotificationKind.RemovedFromOrganization,
                    $"You were removed from {organization.Name}",
                    $"{await MemberQueries.DescribeAsync(db, caller, ct)} removed you from the organization. "
                        + "Its projects, runs and packages are no longer yours to reach.",
                    heldRole is { } held ? $"You held the {held} role." : null,
                    // Not the organization: it is the one page this notification is about and
                    // the one page its reader can no longer open.
                    "/",
                    "Go to your organizations"),
                ct);
        }

        await notifications.PublishToGovernanceAsync(
            organizationId,
            actor: caller,
            new NotificationContent(
                NotificationKind.MemberLeft,
                themselves
                    ? $"{left.Shortly} left {organization.Name}"
                    : $"{left.Shortly} was removed from {organization.Name}",
                themselves
                    ? $"{left} removed themselves from the organization."
                    : $"{await MemberQueries.DescribeAsync(db, caller, ct)} removed {left} from the organization.",
                heldRole is { } role ? $"{left.Shortly} held the {role} role." : null,
                $"/orgs/{organizationId}/members",
                "View members"),
            ct);

        return TypedResults.NoContent();
    }
}
