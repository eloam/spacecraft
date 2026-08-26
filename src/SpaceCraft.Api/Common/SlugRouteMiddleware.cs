using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace SpaceCraft.Api.Common;

/// <summary>
/// Lets organization and project segments in a URL be a slug instead of an id.
/// <para>
/// <c>/organizations/acme-corp/projects/keycloak-admin</c> is a URL someone can read, paste
/// into a message and recognise later; the id form says nothing. Both keep working — the
/// segment is only rewritten when it is not already a GUID.
/// </para>
/// <para>
/// Done here rather than in each handler because every organization-scoped endpoint would
/// otherwise need the same three lines, and one of them would eventually be forgotten. The
/// routes and their <c>:guid</c> constraints stay exactly as they are.
/// </para>
/// </summary>
public sealed class SlugRouteMiddleware(RequestDelegate next, IMemoryCache cache)
{
    private const string Prefix = "/api/v1/organizations/";

    /// <summary>
    /// Slugs are immutable once created, so a resolved id cannot go stale while an entry
    /// lives. Short enough that a deleted organization stops resolving promptly.
    /// </summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    public async Task InvokeAsync(HttpContext context, SpaceCraftDbContext db)
    {
        ArgumentNullException.ThrowIfNull(context);

        var path = context.Request.Path.Value;

        if (path is not null && path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var rewritten = await RewriteAsync(path, db, context.RequestAborted);

            if (rewritten is not null)
            {
                context.Request.Path = rewritten;
            }
        }

        await next(context);
    }

    private async Task<string?> RewriteAsync(string path, SpaceCraftDbContext db, CancellationToken ct)
    {
        // ["", "api", "v1", "organizations", <org>, ...]
        var segments = path.Split('/');

        if (segments.Length < 5 || segments[4].Length == 0)
        {
            return null;
        }

        var changed = false;

        if (!Guid.TryParse(segments[4], out var organizationId))
        {
            var resolved = await ResolveAsync(
                $"org:{segments[4]}",
                () => db.Organizations
                    .AsNoTracking()
                    .Where(o => o.Slug == segments[4])
                    .Select(o => (Guid?)o.Id)
                    .FirstOrDefaultAsync(ct));

            // Left alone when unknown: the endpoint's own 404 is the right answer, and
            // guessing here would turn a missing slug into a routing error instead.
            if (resolved is null)
            {
                return null;
            }

            organizationId = resolved.Value;
            segments[4] = organizationId.ToString();
            changed = true;
        }

        // ["", "api", "v1", "organizations", <org>, "projects", <project>, ...]
        if (segments.Length >= 7
            && segments[5].Equals("projects", StringComparison.OrdinalIgnoreCase)
            && segments[6].Length > 0
            && !Guid.TryParse(segments[6], out _))
        {
            var slug = segments[6];
            var scoped = organizationId;

            var resolved = await ResolveAsync(
                $"proj:{scoped}:{slug}",
                () => db.ApiProjects
                    .AsNoTracking()
                    .Where(p => p.OrganizationId == scoped && p.Slug == slug)
                    .Select(p => (Guid?)p.Id)
                    .FirstOrDefaultAsync(ct));

            if (resolved is null)
            {
                return changed ? string.Join('/', segments) : null;
            }

            segments[6] = resolved.Value.ToString();
            changed = true;
        }

        return changed ? string.Join('/', segments) : null;
    }

    private async Task<Guid?> ResolveAsync(string key, Func<Task<Guid?>> lookup)
    {
        if (cache.TryGetValue(key, out Guid? cached))
        {
            return cached;
        }

        var resolved = await lookup();

        // Negative results are cached too, briefly: a scan for likely slugs should not turn
        // into one database query per guess.
        cache.Set(key, resolved, CacheLifetime);

        return resolved;
    }
}
