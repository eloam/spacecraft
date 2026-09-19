using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Account;

/// <summary>
/// Deletes an account along with everything that is only its: memberships, sessions, reset
/// links, notifications and notification preferences.
/// </summary>
/// <remarks>
/// None of those tables carries a foreign key to the Identity user. The domain does not know
/// the user type, so its rows name the user by id the way a run names its project, and the
/// database would leave them behind — a person's sessions and notification history outliving
/// the person. The same answer as for organizations and projects: the slice that owns the
/// rows removes them, so a reader of this feature sees the whole of what deleting an account
/// takes with it, rather than a cascade declared in the Infrastructure project that the next
/// table to carry a user id would forget to join.
/// <para>
/// Not removed: what the account did rather than what it has. A run keeps who triggered it
/// and an invitation keeps who sent it, both of which stay true after the account is gone;
/// every reader of the inviter already shows "Someone" for an id that no longer resolves.
/// </para>
/// </remarks>
public sealed class AccountDeleter(
    SpaceCraftDbContext db,
    UserManager<SpaceCraftUser> userManager,
    ILogger<AccountDeleter> logger)
{
    public async Task<DeletionRefusal?> DeleteAsync(Guid userId, CancellationToken ct)
    {
        // Two saves — ours, then Identity's — so the transaction is explicit, and the retrying
        // execution strategy has to be handed the whole unit: it refuses to run alongside a
        // transaction it has no way to replay.
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // Every attempt starts from nothing tracked. The strategy replays this unit on the
            // same context after a rollback, and what the previous attempt loaded is stale by
            // then: a membership still marked Deleted, which the aggregate would refuse as
            // "not a member", and an organization holding the token read back from a write
            // the database has since undone, which would fail the replayed update as a
            // conflict. So the unit takes an id and loads everything it touches itself.
            db.ChangeTracker.Clear();

            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
            {
                return new DeletionRefusal("Someone else changed this account at the same time. Reload and try again.");
            }

            // Through each organization rather than by deleting the membership rows, because
            // the rows alone answer to nobody. The blockers the handler checked were counted a
            // query ago; another owner leaving in between is the case the aggregate and the
            // organization's concurrency token exist for — the one refuses on what it loaded,
            // the other refuses a write over what it did not — and neither sees a row removed
            // behind its back.
            var organizations = await db.Organizations
                .Include(o => o.Members)
                .Where(o => o.Members.Any(m => m.UserId == userId))
                .ToListAsync(ct);

            foreach (var organization in organizations)
            {
                try
                {
                    organization.RemoveMember(userId);
                }
                catch (InvalidOperationException ex)
                {
                    return new DeletionRefusal($"{organization.Name}: {ex.Message}");
                }
            }

            await db.SaveChangesAsync(ct);

            // Rows nothing else references, removed in place: the deliveries go with their
            // notification, the one cascade the database does hold.
            await db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
            await db.PasswordResetTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
            await db.Notifications.Where(n => n.UserId == userId).ExecuteDeleteAsync(ct);
            await db.NotificationPreferences.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);

            // Identity saves through the same context, inside this transaction. A failed
            // result is how it reports the user row changed since it was loaded above — a
            // password change landing at the same instant; anything else it throws.
            var result = await userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                return new DeletionRefusal("Someone else changed this account at the same time. Reload and try again.");
            }

            await transaction.CommitAsync(ct);

            logger.LogWarning("Account {UserId} deleted, leaving {Organizations} organization(s)",
                userId, organizations.Count);

            return null;
        });
    }
}
