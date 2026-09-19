using SpaceCraft.Api.Common;
using SpaceCraft.Api.Features.Runs;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Specs;

/// <summary>
/// Removes one spec version, the document behind it, and the runs that were executed
/// against it.
/// </summary>
public sealed class SpecDeleter(
    SpaceCraftDbContext db,
    IObjectStorage storage,
    RunDeleter runs,
    ILogger<SpecDeleter> logger)
{
    /// <summary>
    /// Refused once anything reached a registry: the package stays installable whatever we
    /// delete here, so removing the document it was generated from would leave a published
    /// artifact with no record of where it came from.
    /// </summary>
    public async Task<DeletionRefusal?> DeleteAsync(Guid specVersionId, CancellationToken ct)
    {
        if (await PublishedFromAsync(specVersionId, ct) is { } published)
        {
            return published;
        }

        if (await runs.RefuseWhileActiveAsync(r => r.SpecVersionId == specVersionId, ct) is { } refusal)
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

        var runKeys = await runs.RemoveAsync(r => r.SpecVersionId == specVersionId, ct);

        var spec = await db.SpecVersions.FirstOrDefaultAsync(s => s.Id == specVersionId, ct);
        if (spec is not null)
        {
            db.SpecVersions.Remove(spec);
        }

        // The rows first, as one transaction; the document and the runs' objects only once
        // it has landed, so a save that fails leaves them where their rows still point.
        await db.SaveChangesAsync(ct);

        await storage.TryDeleteAsync(runKeys.Append(spec?.StorageKey), logger);
        return null;
    }

    /// <summary>
    /// Whether a package reached a registry from this spec version, and so why it cannot be
    /// removed or usefully re-uploaded.
    /// </summary>
    /// <remarks>
    /// Succeeded and AlreadyExists both mean the registry carries that version — one because
    /// we pushed it, the other because it was already there. Pending and Failed reached
    /// nothing.
    /// </remarks>
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
}
