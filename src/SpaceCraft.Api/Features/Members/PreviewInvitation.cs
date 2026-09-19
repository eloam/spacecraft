using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Email;

namespace SpaceCraft.Api.Features.Members;

/// <summary>
/// What somebody holding an invitation is shown before deciding.
/// </summary>
/// <remarks>
/// Everything here is already implied by the invitation itself: the email names the
/// organization and who sent it, and accepting would show the rest a moment later. What the
/// projects add is the answer to "is this the right Acme?", which is the question somebody
/// looking at a link from a stranger actually has.
/// </remarks>
public sealed record InvitationPreviewResponse(
    string OrganizationName,
    string OrganizationSlug,
    OrganizationRole Role,
    /// <summary>The name the inviter goes by, or the address they signed up with.</summary>
    string InvitedBy,
    DateTimeOffset ExpiresAt,
    int MemberCount,
    List<string> Projects,
    /// <summary>Whether the signed-in account is already in this organization.</summary>
    bool AlreadyMember);

/// <summary>
/// Says what the invitation is for, and spends nothing.
/// </summary>
/// <remarks>
/// The page this feeds used to accept on arrival: opening the link joined the
/// organization, and the first thing anybody learned about it was that they were in it.
/// An invitation is an offer, and an offer that cannot be read before it is taken is not
/// much of one.
/// <para>
/// The refusal is the same single answer accepting gives — a token that never existed, one
/// already spent, one withdrawn and one that lapsed are not told apart, because doing so
/// would describe an organization to somebody with no standing in it.
/// </para>
/// </remarks>
public static class PreviewInvitation
{
    public static async Task<IResult> HandleAsync(
        [FromBody] AcceptInvitationRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return ApiResults.Invalid(nameof(request.Token), "An invitation token is required.");
        }

        var now = DateTimeOffset.UtcNow;
        var invitation = await db.OrganizationInvitations
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.TokenHash == MailedTokens.Hash(request.Token), ct);

        if (invitation is null || !invitation.IsRedeemable(now))
        {
            return ApiResults.NotFound("A usable invitation");
        }

        var organization = await db.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == invitation.OrganizationId, ct);

        if (organization is null)
        {
            return ApiResults.NotFound("A usable invitation");
        }

        // Names only, and few. Enough to recognise which Acme this is, which is the question
        // somebody looking at a link from a half-remembered colleague actually has.
        var projects = await db.ApiProjects
            .AsNoTracking()
            .Where(p => p.OrganizationId == organization.Id)
            .OrderByDescending(p => p.CreatedAt)
            .Take(5)
            .Select(p => p.Name)
            .ToListAsync(ct);

        var memberCount = await db.OrganizationMembers
            .CountAsync(m => m.OrganizationId == organization.Id, ct);

        var invitedBy = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == invitation.InvitedByUserId)
            .Select(u => u.DisplayName ?? u.Email)
            .FirstOrDefaultAsync(ct);

        var userId = principal.Id();
        var alreadyMember = await db.OrganizationMembers
            .AnyAsync(m => m.OrganizationId == organization.Id && m.UserId == userId, ct);

        return TypedResults.Ok(new InvitationPreviewResponse(
            organization.Name,
            organization.Slug,
            invitation.Role,
            invitedBy ?? "Someone",
            invitation.ExpiresAt,
            memberCount,
            projects,
            alreadyMember));
    }
}
