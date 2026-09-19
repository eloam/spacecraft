using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Common;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Domain.Identity;

namespace SpaceCraft.Api.Features.Auth;

/// <summary>
/// A session is a family of refresh tokens: started by a sign-in, continued by every
/// refresh, ended by whoever revokes it.
/// </summary>
internal static class Sessions
{
    /// <summary>
    /// Issues a token pair and records its refresh half, starting a family or continuing one.
    /// </summary>
    public static async Task<TokenPair> IssueAsync(
        SpaceCraftUser user,
        SpaceCraftDbContext db,
        ITokenFactory tokenFactory,
        Guid? familyId,
        CancellationToken ct)
    {
        // A login starts a family; a refresh continues one. Either way the access token
        // carries the family id, so the session it belongs to is identifiable later.
        var session = familyId ?? Ids.New();
        var pair = tokenFactory.Create(user, session);

        db.RefreshTokens.Add(new RefreshToken(
            user.Id,
            tokenFactory.HashToken(pair.RefreshToken),
            DateTimeOffset.UtcNow.Add(JwtOptions.RefreshTokenLifetime),
            session));

        await db.SaveChangesAsync(ct);
        return pair;
    }

    /// <summary>Ends every session of an account, or every one but the caller's own.</summary>
    public static async Task RevokeAsync(
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
