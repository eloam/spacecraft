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
    /// Time-limited download URL, so the API never proxies large artifact bodies itself.
    /// </summary>
    string GetDownloadUrl(string key, TimeSpan validFor);
}
