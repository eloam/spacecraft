using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Account;

public static class RevokeSession
{
    public static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
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
}
