using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Email;
using SpaceCraft.Api.Features.Members.Responses;
using SpaceCraft.Api.Features.Members.Requests;

namespace SpaceCraft.Api.Features.Members;

/// <summary>
/// Sends an invitation to an address. Nobody joins until it is accepted.
/// </summary>
/// <remarks>
/// This used to resolve the address to whatever account already used it and add that
/// account on the spot. Nothing in this system proves an address belongs to the account
/// holding it — <c>EmailConfirmed</c> is written by a migration and read by nobody — so
/// registering a colleague's work address was enough to be handed their organization the
/// day an admin invited them. The proof now lives where it belongs: a token goes to the
/// mailbox, and only someone who can read that mailbox can accept.
/// <para>
/// The invitation is also written for an address with no account at all, which the old
/// endpoint refused. Being invited is a reason to sign up, and refusing was an account
/// oracle offered to every admin.
/// </para>
/// </remarks>
public static class InviteMember
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        [FromBody] AddMemberRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        BackgroundWorkQueue<InvitationRequest> invitations,
        CancellationToken ct)
    {
        var caller = principal.Id();

        // Admin, not Member: adding people changes who can reach the organization's
        // credentials and publish under its packages.
        var membership = await access.CheckAsync(organizationId, caller, OrganizationRole.Admin, ct);

        if (membership.Denied is { } denied)
        {
            return denied;
        }

        // The address's shape is the request's Validator's business; this is the form it is
        // stored in.
        var address = request.Email.Trim().ToLowerInvariant();

        // Nobody grants a role they do not hold — the same cap the role-change endpoint
        // applies, checked here because the domain knows roles but not who is asking. The
        // role is there behind a pass: the guard refuses a non-member before this line.
        if (request.Role > membership.Role!.Value)
        {
            return ApiResults.Problem(
                "Forbidden",
                $"Granting the {request.Role} role takes that role or higher.",
                StatusCodes.Status403Forbidden);
        }

        var now = DateTimeOffset.UtcNow;

        // Superseded rather than stacked: an older link in an old email stops working the
        // moment a newer invitation is sent, the same rule password resets follow.
        var outstanding = await db.OrganizationInvitations
            .Where(i => i.OrganizationId == organizationId
                        && i.Email == address
                        && i.AcceptedAt == null
                        && i.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var previous in outstanding)
        {
            previous.Revoke(now);
        }

        var raw = OpaqueTokens.Generate();
        var invitation = new OrganizationInvitation(
            organizationId,
            address,
            request.Role,
            caller,
            OpaqueTokens.Hash(raw),
            now.Add(InvitationDispatcher.Lifetime));

        db.OrganizationInvitations.Add(invitation);
        await db.SaveChangesAsync(ct);

        // Written first, sent after: the admin must see the invitation in the listing whether
        // or not a mail relay is having a bad afternoon, and a relay outage must not lose it.
        invitations.Enqueue(new InvitationRequest(invitation.Id, raw));

        return TypedResults.Accepted(
            $"/api/v1/organizations/{organizationId}/members/invitations/{invitation.Id}",
            InvitationResponse.From(invitation));
    }
}
