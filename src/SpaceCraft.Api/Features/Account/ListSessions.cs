using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Account;

public sealed record SessionResponse(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent);

public static class ListSessions
{
    public static async Task<IResult> HandleAsync(
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
}
