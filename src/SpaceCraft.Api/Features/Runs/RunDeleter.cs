using System.Linq.Expressions;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Infrastructure.Persistence;
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
public sealed class RunDeleter(SpaceCraftDbContext db)
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
    /// Stages the runs in scope for removal and returns the keys of the artifacts and step
    /// logs they point at.
    /// </summary>
    /// <remarks>
    /// The rows are staged, not saved: the caller commits them with the rest of its delete,
    /// which keeps one transaction per delete. The objects are handed back rather than removed
    /// here for the same reason — they go once that commit has landed. Removed before it, a
    /// save that failed left rows whose artifacts and logs were already gone.
    /// </remarks>
    public async Task<IReadOnlyList<string?>> RemoveAsync(
        Expression<Func<PipelineRun, bool>> scope, CancellationToken ct)
    {
        var runs = await db.PipelineRuns.Where(scope).Include(r => r.Steps).ToListAsync(ct);

        db.PipelineRuns.RemoveRange(runs);

        return runs
            .Select(r => r.ArtifactStorageKey)
            .Concat(runs.SelectMany(r => r.Steps).Select(s => s.LogStorageKey))
            .ToList();
    }
}
