using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Api.Extensions;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features;

public sealed record AccountResponse(
    Guid Id,
    string Email,
    string? DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    int OrganizationCount);

public sealed record UpdateProfileRequest(string? DisplayName);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record SessionResponse(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent);

public sealed record DeleteAccountRequest(string Password);

/// <summary>Blockers that must be cleared before an account can be deleted.</summary>
public sealed record DeletionBlocker(string OrganizationName, string Reason);

public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/account").WithTags("Account").RequireAuthorization();

        group.MapGet("/", GetAsync)
            .Produces<AccountResponse>()
            .WithSummary("Returns the signed-in user's account.");

        group.MapPatch("/", UpdateProfileAsync)
            .Produces<AccountResponse>()
            .ProducesValidationProblem()
            .WithSummary("Updates the display name.");

        group.MapPost("/password", ChangePasswordAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Changes the password, ending every other session.");

        group.MapGet("/sessions", GetSessionsAsync)
            .Produces<IReadOnlyList<SessionResponse>>()
            .WithSummary("Lists active sessions.");

        group.MapDelete("/sessions/{sessionId:guid}", RevokeSessionAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Signs out one session.");

        group.MapDelete("/sessions", RevokeOtherSessionsAsync)
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Signs out every session except this one.");

        group.MapGet("/deletion-blockers", GetDeletionBlockersAsync)
            .Produces<IReadOnlyList<DeletionBlocker>>()
            .WithSummary("Lists what currently prevents deleting the account.");

        group.MapDelete("/", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem()
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Permanently deletes the account.");

        return api;
    }

    private static async Task<IResult> GetAsync(
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

        var organizations = await db.OrganizationMembers.CountAsync(m => m.UserId == user.Id, ct);

        return TypedResults.Ok(new AccountResponse(
            user.Id, user.Email ?? string.Empty, user.DisplayName,
            user.CreatedAt, user.LastLoginAt, organizations));
    }

    private static async Task<IResult> UpdateProfileAsync(
        [FromBody] UpdateProfileRequest request,
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

        var name = request.DisplayName?.Trim();

        if (name is { Length: > 100 })
        {
            return ApiResults.Invalid(nameof(request.DisplayName), "Use 100 characters or fewer.");
        }

        // Empty clears the name rather than storing whitespace.
        user.DisplayName = string.IsNullOrWhiteSpace(name) ? null : name;
        await userManager.UpdateAsync(user);

        var organizations = await db.OrganizationMembers.CountAsync(m => m.UserId == user.Id, ct);

        return TypedResults.Ok(new AccountResponse(
            user.Id, user.Email ?? string.Empty, user.DisplayName,
            user.CreatedAt, user.LastLoginAt, organizations));
    }

    private static async Task<IResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request,
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

        // The current password is required even though the caller is authenticated: an access
        // token left behind on a shared machine should not be enough to take the account over.
        var result = await userManager.ChangePasswordAsync(
            user, request.CurrentPassword ?? string.Empty, request.NewPassword ?? string.Empty);

        if (!result.Succeeded)
        {
            var wrongCurrent = result.Errors.Any(e => e.Code == "PasswordMismatch");

            return ApiResults.Invalid(
                wrongCurrent ? nameof(request.CurrentPassword) : nameof(request.NewPassword),
                wrongCurrent
                    ? "That is not your current password."
                    : string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        // Same reasoning as a reset: a password change is the moment to end sessions the
        // caller may no longer control. Their own session survives so they are not signed out
        // of the page they are standing on.
        await RevokeSessionsAsync(db, user.Id, except: principal.SessionId(), ct);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetSessionsAsync(
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        var userId = principal.Id();
        var current = principal.SessionId();
        var now = DateTimeOffset.UtcNow;

        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);

        // One family is one sign-in; rotation means several rows describe the same session.
        var sessions = tokens
            .GroupBy(t => t.FamilyId)
            .Select(family => new SessionResponse(
                family.Key,
                family.Min(t => t.CreatedAt),
                family.Max(t => t.CreatedAt),
                family.Max(t => t.ExpiresAt),
                family.Key == current))
            .OrderByDescending(s => s.IsCurrent)
            .ThenByDescending(s => s.LastUsedAt)
            .ToList();

        return TypedResults.Ok(sessions);
    }

    private static async Task<IResult> RevokeSessionAsync(
        Guid sessionId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        var userId = principal.Id();

        var family = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.FamilyId == sessionId && t.RevokedAt == null)
            .ToListAsync(ct);

        if (family.Count == 0)
        {
            return ApiResults.NotFound("The session");
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var token in family)
        {
            token.Revoke(now);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RevokeOtherSessionsAsync(
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        await RevokeSessionsAsync(db, principal.Id(), except: principal.SessionId(), ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetDeletionBlockersAsync(
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct) =>
        TypedResults.Ok(await FindBlockersAsync(db, principal.Id(), ct));

    private static async Task<IResult> DeleteAsync(
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

        var blockers = await FindBlockersAsync(db, user.Id, ct);
        if (blockers.Count > 0)
        {
            // Deleting anyway would leave an organization with no one able to administer it,
            // and its projects unreachable rather than tidied up.
            return ApiResults.Conflict(
                "Hand over or delete these organizations first: "
                + string.Join(", ", blockers.Select(b => b.OrganizationName)) + ".");
        }

        await RevokeSessionsAsync(db, user.Id, except: null, ct);

        var memberships = await db.OrganizationMembers.Where(m => m.UserId == user.Id).ToListAsync(ct);
        db.OrganizationMembers.RemoveRange(memberships);

        var resets = await db.PasswordResetTokens.Where(t => t.UserId == user.Id).ToListAsync(ct);
        db.PasswordResetTokens.RemoveRange(resets);

        await db.SaveChangesAsync(ct);
        await userManager.DeleteAsync(user);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Organizations the user is the only owner of. Those have to be handed over or deleted
    /// first; nothing else about the account blocks deletion.
    /// </summary>
    private static async Task<IReadOnlyList<DeletionBlocker>> FindBlockersAsync(
        SpaceCraftDbContext db, Guid userId, CancellationToken ct)
    {
        var ownerships = await db.OrganizationMembers
            .Where(m => m.UserId == userId && m.Role == OrganizationRole.Owner)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);

        if (ownerships.Count == 0)
        {
            return [];
        }

        var soleOwned = await db.OrganizationMembers
            .Where(m => ownerships.Contains(m.OrganizationId) && m.Role == OrganizationRole.Owner)
            .GroupBy(m => m.OrganizationId)
            .Where(g => g.Count() == 1)
            .Select(g => g.Key)
            .ToListAsync(ct);

        if (soleOwned.Count == 0)
        {
            return [];
        }

        return await db.Organizations
            .Where(o => soleOwned.Contains(o.Id))
            .Select(o => new DeletionBlocker(o.Name, "You are its only owner."))
            .ToListAsync(ct);
    }

    private static async Task RevokeSessionsAsync(
        SpaceCraftDbContext db, Guid userId, Guid? except, CancellationToken ct)
    {
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null
                        && (except == null || t.FamilyId != except))
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var token in tokens)
        {
            token.Revoke(now);
        }

        await db.SaveChangesAsync(ct);
    }
}
