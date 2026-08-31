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
    /// How long a resolution is trusted when nothing has invalidated it.
    /// <para>
    /// This used to read "slugs are immutable once created", which the domain contradicts:
    /// <c>Rename</c> recomputes the slug from the new name. Entries are now dropped the moment
    /// a save moves a slug — see <see cref="SlugCacheInvalidator"/> — so this is a ceiling on
    /// how long an entry may sit unused, not the window in which it may be wrong.
    /// </para>
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

            await RewriteProjectQueryAsync(context, db, context.RequestAborted);
        }

        await next(context);
    }

    /// <summary>
    /// Translates a ?projectId= slug the same way path segments are translated.
    /// <para>
    /// Endpoints that filter by project take the id as a query parameter, and a front end
    /// routing on slugs has only the slug to hand. Without this the value fails to bind and
    /// the request fails as a server error rather than a filter that found nothing.
    /// </para>
    /// </summary>
    private async Task RewriteProjectQueryAsync(HttpContext context, SpaceCraftDbContext db, CancellationToken ct)
    {
        var raw = context.Request.Query["projectId"].ToString();

        if (string.IsNullOrEmpty(raw) || Guid.TryParse(raw, out _))
        {
            return;
        }

        // The path has already been rewritten, so the organization segment is an id.
        var segments = context.Request.Path.Value!.Split('/');

        if (segments.Length < 5 || !Guid.TryParse(segments[4], out var organizationId))
        {
            return;
        }

        var resolved = await ResolveAsync(
            SlugCacheKeys.Project(organizationId, raw),
            () => db.ApiProjects
                .AsNoTracking()
                .Where(p => p.OrganizationId == organizationId && p.Slug == raw)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(ct));

        if (resolved is null)
        {
            return;
        }

        var query = context.Request.Query
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        query["projectId"] = resolved.Value.ToString();

        context.Request.QueryString = QueryString.Create(
            query.SelectMany(pair => pair.Value.Select(v => new KeyValuePair<string, string?>(pair.Key, v))));
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
                SlugCacheKeys.Organization(segments[4]),
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
                SlugCacheKeys.Project(scoped, slug),
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
