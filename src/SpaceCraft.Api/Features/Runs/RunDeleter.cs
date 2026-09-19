using System.Linq.Expressions;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Runs;

/// <summary>
/// Removes the runs of something being deleted, and refuses while any of them is still going.
/// </summary>
/// <remarks>
/// A run carries the ids of its organization, project, target and spec version without a
/// foreign key behind any of them, so the database would leave the rows behind — and the
/// artifacts and step logs they point at with them. Everything that deletes one of those
/// containers comes through here for its runs.
/// </remarks>
public sealed class RunDeleter(
    SpaceCraftDbContext db,
    IObjectStorage storage,
    ILogger<RunDeleter> logger)
{
    /// <summary>
    /// Refuses while a run in scope is queued or executing. A worker holding a lease would
    /// carry on against rows that no longer exist, and its failure would be reported nowhere.
    /// </summary>
    public async Task<DeletionRefusal?> RefuseWhileActiveAsync(
        Expression<Func<PipelineRun, bool>> scope, CancellationToken ct)
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

    /// <summary>
    /// Removes the runs in scope along with their artifacts and step logs. The rows are
    /// staged, not saved: the caller commits them with the rest of its delete.
    /// </summary>
    public async Task PurgeAsync(Expression<Func<PipelineRun, bool>> scope, CancellationToken ct)
    {
        var runs = await db.PipelineRuns.Where(scope).Include(r => r.Steps).ToListAsync(ct);

        var keys = runs
            .Select(r => r.ArtifactStorageKey)
            .Concat(runs.SelectMany(r => r.Steps).Select(s => s.LogStorageKey));

        await storage.TryDeleteAsync(keys, logger, ct);

        db.PipelineRuns.RemoveRange(runs);
    }
}
