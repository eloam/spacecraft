using SpaceCraft.Api.Common;
using SpaceCraft.Api.Features.Runs;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Projects;

/// <summary>
/// Deletes a project, or one of its SDK targets, along with what it owns.
/// </summary>
/// <remarks>
/// Specs and targets hang off the project by foreign key. Runs and publications carry its
/// ids without one, and the stored documents, artifacts and logs are blobs whose only
/// reference is the row being deleted — so nothing else will ever reach them again.
/// </remarks>
public sealed class ProjectDeleter(
    SpaceCraftDbContext db,
    IObjectStorage storage,
    RunDeleter runs,
    ILogger<ProjectDeleter> logger)
{
    public async Task<DeletionRefusal?> DeleteAsync(Guid projectId, CancellationToken ct)
    {
        if (await runs.RefuseWhileActiveAsync(r => r.ApiProjectId == projectId, ct) is { } refusal)
        {
            return refusal;
        }

        var specKeys = await db.SpecVersions
            .Where(s => s.ApiProjectId == projectId)
            .Select(s => s.StorageKey)
            .ToListAsync(ct);

        var runKeys = await runs.RemoveAsync(r => r.ApiProjectId == projectId, ct);

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

        // The rows first, as one transaction; the objects only once it has landed, so a save
        // that fails leaves the documents, artifacts and logs where their rows still point.
        await db.SaveChangesAsync(ct);

        await storage.TryDeleteAsync(specKeys.Concat(runKeys), logger);
        return null;
    }

    public async Task<DeletionRefusal?> DeleteTargetAsync(Guid targetId, CancellationToken ct)
    {
        if (await runs.RefuseWhileActiveAsync(r => r.SdkTargetId == targetId, ct) is { } refusal)
        {
            return refusal;
        }

        var runKeys = await runs.RemoveAsync(r => r.SdkTargetId == targetId, ct);

        db.Publications.RemoveRange(
            await db.Publications.Where(p => p.SdkTargetId == targetId).ToListAsync(ct));

        var target = await db.SdkTargets.FirstOrDefaultAsync(t => t.Id == targetId, ct);
        if (target is not null)
        {
            db.SdkTargets.Remove(target);
        }

        await db.SaveChangesAsync(ct);

        await storage.TryDeleteAsync(runKeys, logger);
        return null;
    }
}
