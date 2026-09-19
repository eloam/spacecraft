using System.Security.Claims;
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
/// Joins the organization the token was issued for.
/// </summary>
/// <remarks>
/// The token is the whole proof, and it proves the one thing that was missing: whoever
/// presents it could read the mailbox the invitation was sent to. Deliberately not checked
/// against the signed-in account's own address — that address has never been verified
/// either, so matching it would be checking one unproven claim against another. Forwarding
/// an invitation to a colleague is also a thing people do, and the organization's admin can
/// see who accepted.
/// </remarks>
public static class AcceptInvitation
{
    public static async Task<IResult> HandleAsync(
        [FromBody] AcceptInvitationRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        INotificationPublisher notifications,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var hash = MailedTokens.Hash(request.Token);

        // Looked up by hash, so the comparison is an index seek rather than a string compare
        // whose timing could be measured.
        var invitation = await db.OrganizationInvitations.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);

        // One answer for a token that never existed, one that was spent, one that was
        // withdrawn and one that lapsed. Telling them apart would say something about an
        // organization to somebody who has no standing in it.
        if (invitation is null || !invitation.IsRedeemable(now))
        {
            return ApiResults.NotFound("A usable invitation");
        }

        var organization = await db.Organizations
            .Include(o => o.Members)
            .FirstOrDefaultAsync(o => o.Id == invitation.OrganizationId, ct);

        if (organization is null)
        {
            // The organization went away while the invitation sat in a mailbox.
            invitation.Revoke(now);
            await db.SaveChangesAsync(ct);

            return ApiResults.NotFound("A usable invitation");
        }

        var userId = principal.Id();

        if (organization.Members.Any(m => m.UserId == userId))
        {
            // Spending the invitation anyway, so a second copy of the email cannot be held
            // in reserve against a later change of role.
            invitation.Accept(userId, now);
            await db.SaveChangesAsync(ct);

            return ApiResults.Conflict("You are already a member of this organization.");
        }

        try
        {
            organization.AddMember(userId, invitation.Role);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResults.Conflict(ex.Message);
        }

        invitation.Accept(userId, now);
        await db.SaveChangesAsync(ct);

        // After the save, so nothing is announced that did not happen. The joiner is the
        // actor and hears nothing: they are looking at the result of their own click.
        var joined = await MemberQueries.DescribeAsync(db, userId, ct);

        await notifications.PublishToGovernanceAsync(
            organization.Id,
            actor: userId,
            new NotificationContent(
                NotificationKind.MemberJoined,
                $"{joined.Shortly} joined {organization.Name}",
                $"{joined} accepted an invitation and holds the {invitation.Role} role.",
                Detail: null,
                $"/orgs/{organization.Id}/members",
                "View members"),
            ct);

        var members = await MemberQueries.ListAsync(db, invitation.OrganizationId, userId, ct);
        return TypedResults.Ok(members.First(m => m.UserId == userId));
    }
}
