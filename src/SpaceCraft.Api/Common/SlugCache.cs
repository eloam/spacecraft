using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Api.Common;

/// <summary>
/// The cache keys <see cref="SlugRouteMiddleware"/> resolves under, written once so the
/// middleware that fills the cache and the interceptor that empties it cannot disagree about
/// what an entry is called.
/// </summary>
public static class SlugCacheKeys
{
    public static string Organization(string slug) => $"org:{slug}";

    public static string Project(Guid organizationId, string slug) => $"proj:{organizationId}:{slug}";
}

/// <summary>
/// Drops slug cache entries the moment a save makes them wrong.
/// <para>
/// The cache used to rest on "slugs are immutable once created", which the domain contradicts
/// in as many words: <c>Rename</c> recomputes the slug from the new name, and the comment on
/// <see cref="SpaceCraft.Domain.Organizations.Organization.Rename"/> says links to the old one
/// stop resolving. Two things went wrong for the lifetime of an entry. A freed slug taken by
/// a sibling still resolved to its previous owner, so a request naming one project was
/// answered — and written to — by another. And a negative entry outlived the creation it was
/// recorded before, so a project created just after someone probed its name was unreachable
/// by that name.
/// </para>
/// <para>
/// Done here rather than at the handful of endpoints that rename and create, for the reason
/// the middleware itself gives for existing: one of them would eventually be forgotten. The
/// change tracker already knows which slugs moved and what they moved from, so nothing has to
/// be remembered by hand — this is the same bargain as stamping UpdatedAt in SaveChangesAsync.
/// </para>
/// </summary>
public sealed class SlugCacheInvalidator(IMemoryCache cache) : SaveChangesInterceptor
{
    /// <summary>
    /// Collected while the change tracker still knows what moved, evicted once the write has
    /// actually landed — dropping them earlier would let a concurrent request refill the
    /// cache from a transaction that then rolled back.
    /// </summary>
    private readonly List<string> _stale = [];

    /// <summary>Named identically on <see cref="Organization"/> and <see cref="ApiProject"/>.</summary>
    private const string SlugProperty = "Slug";

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        foreach (var key in _stale)
        {
            cache.Remove(key);
        }

        _stale.Clear();
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        // Nothing landed, so nothing cached is wrong yet.
        _stale.Clear();
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Collect(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<Organization>())
        {
            foreach (var slug in MovedSlugs(entry))
            {
                _stale.Add(SlugCacheKeys.Organization(slug));
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<ApiProject>())
        {
            foreach (var slug in MovedSlugs(entry))
            {
                _stale.Add(SlugCacheKeys.Project(entry.Entity.OrganizationId, slug));
            }
        }
    }

    /// <summary>
    /// Every slug this entry invalidates: the one it is taking on, the one it is giving up,
    /// or both when it is renaming between them.
    /// </summary>
    /// <remarks>
    /// A rename has to evict both ends. The old key would otherwise keep pointing at this row
    /// after the name moved on, and the new key may hold a cached "no such slug" from before
    /// this row claimed it.
    /// </remarks>
    private static IEnumerable<string> MovedSlugs<T>(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> entry)
        where T : class
    {
        // Both routed aggregates spell it the same, which is what lets one method read either.
        var slug = entry.Property(SlugProperty);

        switch (entry.State)
        {
            case EntityState.Added:
            case EntityState.Deleted:
                yield return (string)slug.CurrentValue!;
                break;

            case EntityState.Modified:
                var before = (string?)slug.OriginalValue;
                var after = (string?)slug.CurrentValue;

                if (!string.Equals(before, after, StringComparison.Ordinal))
                {
                    if (before is not null)
                    {
                        yield return before;
                    }

                    if (after is not null)
                    {
                        yield return after;
                    }
                }

                break;
        }
    }
}
