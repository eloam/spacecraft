using SpaceCraft.Infrastructure.Storage;

namespace SpaceCraft.Api.Common;

public static class ObjectStorageExtensions
{
    /// <summary>
    /// Removes stored objects whose only reference is a row that has just been deleted,
    /// carrying on past any it cannot remove.
    /// </summary>
    /// <remarks>
    /// Called after the rows have been committed, never before: a blob removed ahead of a save
    /// that then fails is content gone from under a row that stays. A blob we cannot remove
    /// afterwards is wasted space, not a reason to report the delete as failed when the rows
    /// are already gone. It is logged and left behind.
    /// <para>
    /// No cancellation token, for the same reason. By the time this runs the commit has landed,
    /// so a caller who disconnected has nothing left to cancel — stopping here would only turn
    /// their cancellation into orphaned objects.
    /// </para>
    /// </remarks>
    public static async Task TryDeleteAsync(
        this IObjectStorage storage, IEnumerable<string?> keys, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(keys);

        foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k)))
        {
            try
            {
                await storage.DeleteAsync(key!, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete stored object {Key}", key);
            }
        }
    }
}
