using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Notifications;
using SpaceCraft.Infrastructure.Notifications;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Email;
using SpaceCraft.Api.Features.Members.Requests;

namespace SpaceCraft.Api.Features.Members;

/// <summary>
/// Turns the invitation down.
/// </summary>
/// <remarks>
/// Recorded as a decline rather than as a revocation: the admin withdrawing an invitation
/// and the recipient refusing it are different events, and only the second is an answer.
/// Either way the link stops working, which is what the person clicking this wants.
/// </remarks>
public static class DeclineInvitation
{
    public static async Task<IResult> HandleAsync(
        [FromBody] AcceptInvitationRequest request,
        SpaceCraftDbContext db,
        INotificationPublisher notifications,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        var invitation = await db.OrganizationInvitations
            .FirstOrDefaultAsync(i => i.TokenHash == MailedTokens.Hash(request.Token), ct);

        if (invitation is null || !invitation.IsRedeemable(now))
        {
            return ApiResults.NotFound("A usable invitation");
        }

        invitation.Decline(now);
        await db.SaveChangesAsync(ct);

        var organization = await db.Organizations
            .AsNoTracking()
            .Where(o => o.Id == invitation.OrganizationId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync(ct);

        // To the people who answer for the organization rather than to the sender alone: an
        // invitation is the organization's, and whoever picks up the thread next needs to know
        // it was refused rather than still travelling. Nobody is excluded — the person who
        // declined is not a member and hears nothing.
        await notifications.PublishToGovernanceAsync(
            invitation.OrganizationId,
            actor: null,
            new NotificationContent(
                NotificationKind.InvitationDeclined,
                $"{invitation.Email} declined the invitation to {organization ?? "your organization"}",
                // Names them again rather than saying "they": the reader may be an admin who
                // did not send this one, and the answer to "who?" should not depend on having
                // read the line above.
                $"{invitation.Email} was invited as {invitation.Role} and turned it down.",
                "Nobody has been added to the organization. Send another invitation if they "
                    + "should still have access.",
                $"/orgs/{invitation.OrganizationId}/members",
                "View members"),
            ct);

        return TypedResults.NoContent();
    }
}
