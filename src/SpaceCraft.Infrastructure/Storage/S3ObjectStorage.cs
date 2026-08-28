using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Infrastructure.Storage;

/// <summary>
/// S3-compatible implementation, pointed at Scaleway Object Storage through
/// <see cref="ObjectStorageOptions.ServiceUrl"/> — a bucket per environment, the same
/// implementation everywhere.
/// </summary>
public sealed class S3ObjectStorage(IAmazonS3 client, IOptions<ObjectStorageOptions> options) : IObjectStorage
{
    private readonly ObjectStorageOptions _options = options.Value;

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            // Let the SDK compute the checksum so a truncated upload is rejected by the server.
            DisablePayloadSigning = false,
        };

        await client.PutObjectAsync(request, ct);
    }

    public async Task PutTextAsync(string key, string content, string contentType, CancellationToken ct = default)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await PutAsync(key, stream, contentType, ct);
    }

    public async Task<Stream> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await client.GetObjectAsync(_options.BucketName, key, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ObjectNotFoundException(key, ex);
        }
    }

    public async Task<string> GetTextAsync(string key, CancellationToken ct = default)
    {
        await using var stream = await GetAsync(key, ct);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await client.GetObjectMetadataAsync(_options.BucketName, key, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct = default) =>
        client.DeleteObjectAsync(_options.BucketName, key, ct);

    public async Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationKey);

        try
        {
            await client.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = _options.BucketName,
                SourceKey = sourceKey,
                DestinationBucket = _options.BucketName,
                DestinationKey = destinationKey,
            }, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Same shape as GetAsync: a caller relocating objects needs to tell "gone" from
            // "the request was wrong", and both arrive here as a 404.
            throw new ObjectNotFoundException(sourceKey, ex);
        }
    }

    public string GetDownloadUrl(string key, TimeSpan validFor) =>
        client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            Expires = DateTime.UtcNow.Add(validFor),
            Verb = HttpVerb.GET,
        });
}

public sealed class ObjectNotFoundException(string key, Exception? inner = null)
    : Exception($"Object '{key}' was not found in storage.", inner)
{
    public string Key { get; } = key;
}
