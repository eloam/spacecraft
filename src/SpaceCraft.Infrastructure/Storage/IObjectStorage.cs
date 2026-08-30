namespace SpaceCraft.Infrastructure.Storage;

/// <summary>
/// Blob persistence for specs, build artifacts and logs. These can run to tens of
/// megabytes each, which is why they never touch Postgres.
/// </summary>
public interface IObjectStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    Task PutTextAsync(string key, string content, string contentType, CancellationToken ct = default);

    /// <summary>Opens the object for reading. Caller owns the returned stream.</summary>
    Task<Stream> GetAsync(string key, CancellationToken ct = default);

    Task<string> GetTextAsync(string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Copies an object within the bucket, server-side. Used to move objects to a new key
    /// layout without pulling tens of megabytes through the process to push them straight
    /// back. Throws <see cref="ObjectNotFoundException"/> when the source is absent.
    /// </summary>
    Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default);
}
