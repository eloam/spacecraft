using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Deletion;

/// <summary>Why a delete was refused, so the caller can say something specific.</summary>
public sealed record DeletionRefusal(string Reason);

/// <summary>
/// Deletes organizations, projects and SDK targets along with everything they own.
/// <para>
/// Cascade deletes cover only part of the graph: projects, members, targets and specs hang
/// off their parent by foreign key, but pipeline runs, registry credentials and publications
/// carry an organization id without a constraint behind it. Relying on the database alone
/// would leave those behind — including encrypted registry tokens outliving the organization
/// they belonged to, which is the one kind of leftover that actually matters.
/// </para>
/// <para>
/// Stored objects are removed too. A spec, an artifact and a step log are all blobs whose
/// only reference is the row being deleted, so nothing else will ever reach them again.
/// </para>
/// </summary>
public sealed class ResourceDeleter(
    SpaceCraftDbContext db,
    IObjectStorage storage,
    ILogger<ResourceDeleter> logger)
{
    public async Task<DeletionRefusal?> DeleteOrganizationAsync(Guid organizationId, CancellationToken ct)
    {
        if (await HasActiveRunsAsync(r => r.OrganizationId == organizationId, ct) is { } refusal)
        {
            return refusal;
        }

        var projectIds = await db.ApiProjects
            .Where(p => p.OrganizationId == organizationId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        foreach (var projectId in projectIds)
        {
            await PurgeProjectContentAsync(projectId, ct);
        }

        await PurgeRunsAsync(r => r.OrganizationId == organizationId, ct);

        // No foreign key covers these two, so nothing would remove them for us.
        db.RegistryCredentials.RemoveRange(
            await db.RegistryCredentials.Where(c => c.OrganizationId == organizationId).ToListAsync(ct));
        db.Publications.RemoveRange(
            await db.Publications.Where(p => p.OrganizationId == organizationId).ToListAsync(ct));

        var organization = await db.Organizations
            .Include(o => o.Members)
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        if (organization is not null)
        {
            db.Organizations.Remove(organization);
        }

        await db.SaveChangesAsync(ct);

        logger.LogWarning("Organization {OrganizationId} deleted with {Projects} project(s)",
            organizationId, projectIds.Count);

        return null;
    }

    public async Task<DeletionRefusal?> DeleteProjectAsync(Guid projectId, CancellationToken ct)
    {
        if (await HasActiveRunsAsync(r => r.ApiProjectId == projectId, ct) is { } refusal)
        {
            return refusal;
        }

        await PurgeProjectContentAsync(projectId, ct);
        await PurgeRunsAsync(r => r.ApiProjectId == projectId, ct);

        var targetIds = await db.SdkTargets
            .Where(t => t.ApiProjectId == projectId)
            .Select(t => t.Id)
            .ToListAsync(ct);

        db.Publications.RemoveRange(
            await db.Publications.Where(p => targetIds.Contains(p.SdkTargetId)).ToListAsync(ct));

        var project = await db.ApiProjects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is not null)
        {
            db.ApiProjects.Remove(project);
        }

        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<DeletionRefusal?> DeleteTargetAsync(Guid targetId, CancellationToken ct)
    {
        if (await HasActiveRunsAsync(r => r.SdkTargetId == targetId, ct) is { } refusal)
        {
            return refusal;
        }

        await PurgeRunsAsync(r => r.SdkTargetId == targetId, ct);

        db.Publications.RemoveRange(
            await db.Publications.Where(p => p.SdkTargetId == targetId).ToListAsync(ct));

        var target = await db.SdkTargets.FirstOrDefaultAsync(t => t.Id == targetId, ct);
        if (target is not null)
        {
            db.SdkTargets.Remove(target);
        }

        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// Removes one spec version, the document behind it, and the runs that were executed
    /// against it.
    ///
    /// Refused once anything reached a registry: the package stays installable whatever we
    /// delete here, so removing the document it was generated from would leave a published
    /// artifact with no record of where it came from.
    /// </summary>
    public async Task<DeletionRefusal?> DeleteSpecVersionAsync(Guid specVersionId, CancellationToken ct)
    {
        if (await PublishedFromAsync(specVersionId, ct) is { } published)
        {
            return published;
        }

        if (await HasActiveRunsAsync(r => r.SpecVersionId == specVersionId, ct) is { } refusal)
        {
            return refusal;
        }

        var runIds = await db.PipelineRuns
            .Where(r => r.SpecVersionId == specVersionId)
            .Select(r => r.Id)
            .ToListAsync(ct);

        // Pending and failed attempts only; the succeeded ones were refused above. Nothing
        // else removes them, since no foreign key covers publications.
        db.Publications.RemoveRange(
            await db.Publications.Where(p => runIds.Contains(p.PipelineRunId)).ToListAsync(ct));

        await PurgeRunsAsync(r => r.SpecVersionId == specVersionId, ct);

        var spec = await db.SpecVersions.FirstOrDefaultAsync(s => s.Id == specVersionId, ct);
        if (spec is not null)
        {
            await DeleteObjectsAsync([spec.StorageKey], ct);
            db.SpecVersions.Remove(spec);
        }

        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// Whether a package reached a registry from this spec version, and so why it cannot be
    /// removed or usefully re-uploaded.
    ///
    /// Succeeded and AlreadyExists both mean the registry carries that version — one because
    /// we pushed it, the other because it was already there. Pending and Failed reached
    /// nothing.
    /// </summary>
    public async Task<DeletionRefusal?> PublishedFromAsync(Guid specVersionId, CancellationToken ct)
    {
        // Matched on what a publication is *about* rather than on which run last touched it.
        //
        // One row exists per (target, version), and a retry of the same version reuses it —
        // moving its PipelineRunId to the newer run. Joining on that id therefore lost sight
        // of the publication as soon as it was retried, and this guard concluded that nothing
        // had ever been published from the document. The spec a live package was generated
        // from could then be deleted.
        var produced = await db.PipelineRuns
            .Where(r => r.SpecVersionId == specVersionId && r.ResolvedVersion != null)
            .Select(r => new { r.SdkTargetId, r.ResolvedVersion })
            .Distinct()
            .ToListAsync(ct);

        if (produced.Count == 0) return null;

        var targetIds = produced.Select(p => p.SdkTargetId).Distinct().ToList();

        var candidates = await db.Publications
            .Where(p => p.Status == PublicationStatus.Succeeded || p.Status == PublicationStatus.AlreadyExists)
            .Where(p => targetIds.Contains(p.SdkTargetId))
            .Select(p => new { p.SdkTargetId, p.PackageName, p.Version })
            .ToListAsync(ct);

        var versionsByTarget = produced
            .GroupBy(p => p.SdkTargetId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ResolvedVersion!).ToHashSet(StringComparer.Ordinal));

        var published = candidates
            .Where(c => versionsByTarget.TryGetValue(c.SdkTargetId, out var versions)
                        && versions.Contains(c.Version))
            .Select(c => new { c.PackageName, c.Version })
            .ToList();

        if (published.Count == 0) return null;

        var packages = string.Join(", ", published
            .Select(p => $"{p.PackageName}@{p.Version}")
            .Distinct(StringComparer.Ordinal));

        return new DeletionRefusal(
            $"This document was published as {packages}. Registries are append-only, so the "
            + "package stays available whatever happens here.");
    }

    /// <summary>
    /// Refuses while a run is queued or executing. A worker holding a lease would carry on
    /// against rows that no longer exist, and its failure would be reported nowhere.
    /// </summary>
    private async Task<DeletionRefusal?> HasActiveRunsAsync(
        System.Linq.Expressions.Expression<Func<PipelineRun, bool>> scope, CancellationToken ct)
    {
        var active = await db.PipelineRuns
            .Where(scope)
            .CountAsync(r => r.Status == RunStatus.Queued || r.Status == RunStatus.Running, ct);

        return active == 0
            ? null
            : new DeletionRefusal(
                $"{active} run{(active == 1 ? " is" : "s are")} still queued or running. "
                + "Wait for them to finish, or cancel them first.");
    }

    /// <summary>Removes the specs of a project, and the stored documents behind them.</summary>
    private async Task PurgeProjectContentAsync(Guid projectId, CancellationToken ct)
    {
        var specKeys = await db.SpecVersions
            .Where(s => s.ApiProjectId == projectId)
            .Select(s => s.StorageKey)
            .ToListAsync(ct);

        await DeleteObjectsAsync(specKeys, ct);
    }

    /// <summary>Removes runs in scope, along with their artifacts and step logs.</summary>
    private async Task PurgeRunsAsync(
        System.Linq.Expressions.Expression<Func<PipelineRun, bool>> scope, CancellationToken ct)
    {
        var runs = await db.PipelineRuns.Where(scope).Include(r => r.Steps).ToListAsync(ct);

        var keys = runs
            .Select(r => r.ArtifactStorageKey)
            .Concat(runs.SelectMany(r => r.Steps).Select(s => s.LogStorageKey))
            .Where(key => key is not null)
            .Select(key => key!)
            .ToList();

        await DeleteObjectsAsync(keys, ct);

        db.PipelineRuns.RemoveRange(runs);
    }

    private async Task DeleteObjectsAsync(IEnumerable<string> keys, CancellationToken ct)
    {
        foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k)))
        {
            try
            {
                await storage.DeleteAsync(key, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A blob we cannot remove is wasted space, not a reason to abandon the
                // delete and leave the caller with a half-removed organization.
                logger.LogWarning(ex, "Could not delete stored object {Key}", key);
            }
        }
    }
}
