using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Auth;

/// <summary>
/// The reset links an account has outstanding: issued by the dispatcher, put out by whoever
/// makes them dangerous to leave lying around.
/// </summary>
internal static class PasswordResets
{
    /// <summary>
    /// Invalidates every reset link this account still has outstanding, so that only what
    /// happens next can change the password.
    /// </summary>
    /// <remarks>
    /// Staged, not saved: the caller decides what else goes in the same save. The dispatcher
    /// adds the link that supersedes them; a password change has nothing to add and saves
    /// on its own. Two callers with the same query used to keep their own copy of it.
    /// </remarks>
    public static async Task InvalidateAsync(
        SpaceCraftDbContext db, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var outstanding = await db.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null && t.InvalidatedAt == null)
            .ToListAsync(ct);

        foreach (var token in outstanding)
        {
            token.Invalidate(now);
        }
    }
}
