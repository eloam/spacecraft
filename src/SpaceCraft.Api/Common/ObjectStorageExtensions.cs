using SpaceCraft.Infrastructure.Storage;

namespace SpaceCraft.Api.Common;

public static class ObjectStorageExtensions
{
    /// <summary>
    /// Removes stored objects whose only reference is a row being deleted, carrying on past
    /// any it cannot remove.
    /// </summary>
    /// <remarks>
    /// A blob we cannot remove is wasted space, not a reason to abandon the delete and leave
    /// the caller with a half-removed organization. It is logged and left behind.
    /// </remarks>
    public static async Task TryDeleteAsync(
        this IObjectStorage storage, IEnumerable<string?> keys, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(keys);

        foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k)))
        {
            try
            {
                await storage.DeleteAsync(key!, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not delete stored object {Key}", key);
            }
        }
    }
}
