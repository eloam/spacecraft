using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Features.Auth.Requests;

namespace SpaceCraft.Api.Features.Auth;

public static class Logout
{
    public static async Task<IResult> HandleAsync(
        [FromBody] RefreshRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        var userId = principal.Id();
        var hash = OpaqueTokens.Hash(request.RefreshToken);

        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == userId, ct);

        if (stored is not null)
        {
            var now = DateTimeOffset.UtcNow;
            var family = await db.RefreshTokens.Where(t => t.FamilyId == stored.FamilyId).ToListAsync(ct);
            foreach (var token in family)
            {
                token.Revoke(now);
            }

            await db.SaveChangesAsync(ct);
        }

        // An absent token is a 400 from the request's Validator, never from here. A present
        // one gets 204 whether it existed or not: that is not the caller's business.
        return TypedResults.NoContent();
    }
}
