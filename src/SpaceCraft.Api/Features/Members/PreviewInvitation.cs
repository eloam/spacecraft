using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Features.Members.Responses;
using SpaceCraft.Api.Features.Members.Requests;

namespace SpaceCraft.Api.Features.Members;

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
    public static async Task<Results<Ok<InvitationPreviewResponse>, ProblemHttpResult, ValidationProblem>> HandleAsync(
        [FromBody] AcceptInvitationRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        var invitation = await db.OrganizationInvitations
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.TokenHash == OpaqueTokens.Hash(request.Token), ct);

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
