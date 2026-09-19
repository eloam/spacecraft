using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpaceCraft.Domain.Identity;

namespace SpaceCraft.Api.Features.Auth;

public static class RefreshSession
{
    public static async Task<IResult> HandleAsync(
        [FromBody] RefreshRequest request,
        SpaceCraftDbContext db,
        UserManager<SpaceCraftUser> userManager,
        ITokenFactory tokenFactory,
        IOptions<JwtOptions> jwtOptions,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return ApiResults.Invalid(nameof(request.RefreshToken), "A refresh token is required.");
        }

        var hash = tokenFactory.HashToken(request.RefreshToken);
        var now = DateTimeOffset.UtcNow;

        // Claimed by the database, in one statement, rather than read and then written.
        //
        // Reading the row, finding UsedAt null and marking it used forty lines later is two
        // operations with a gap, and the gap was the whole defence: two requests carrying the
        // same token both saw it unused, both succeeded, and one family became two live
        // chains. After that the token had been redeemed twice without ever being *seen*
        // twice, so the reuse detection below — the only thing between a stolen refresh token
        // and an indefinite session — could never fire again for that family.
        //
        // The condition carries the whole of IsActive, so a revoked or expired token loses
        // the race to the same statement that would otherwise have granted it.
        var claimed = await db.RefreshTokens
            .Where(t => t.TokenHash == hash
                        && t.UsedAt == null
                        && t.RevokedAt == null
                        && t.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.UsedAt, now)
                    .SetProperty(t => t.UpdatedAt, now),
                ct);

        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null)
        {
            return ApiResults.Problem("Invalid token", "The refresh token is not recognised.", StatusCodes.Status401Unauthorized);
        }

        // Lost the race but the token was never spent — revoked, or expired. Spent is the
        // dangerous case and keeps its own answer below.
        if (claimed == 0 && stored.UsedAt is null)
        {
            return ApiResults.Problem("Expired token", "The refresh token has expired or been revoked.", StatusCodes.Status401Unauthorized);
        }

        // A token presented twice means the first holder's copy leaked. Revoking the whole
        // family logs out the attacker and the legitimate user, which is the safe outcome.
        if (claimed == 0)
        {
            var family = await db.RefreshTokens.Where(t => t.FamilyId == stored.FamilyId).ToListAsync(ct);
            foreach (var token in family)
            {
                token.Revoke(now);
            }

            await db.SaveChangesAsync(ct);

            return ApiResults.Problem(
                "Token reuse detected",
                "This refresh token has already been used. All sessions in its family have been revoked.",
                StatusCodes.Status401Unauthorized);
        }

        var user = await userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null)
        {
            return ApiResults.Problem("Invalid token", "The refresh token is not recognised.", StatusCodes.Status401Unauthorized);
        }

        // Already marked by the claim above; reloaded so the tracked entity matches the row.
        await db.Entry(stored).ReloadAsync(ct);

        var pair = await Sessions.IssueAsync(user, db, tokenFactory, stored.FamilyId, ct);
        return TypedResults.Ok(AuthResponse.From(pair, user));
    }
}
