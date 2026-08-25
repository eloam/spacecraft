using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Infrastructure.Storage;

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    [Required]
    public string BucketName { get; set; } = "spacecraft";

    /// <summary>
    /// S3-compatible endpoint. Set for MinIO/R2; leave empty to use real AWS S3.
    /// </summary>
    public string? ServiceUrl { get; set; }

    public string Region { get; set; } = "us-east-1";

    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    /// <summary>MinIO requires path-style addressing; AWS S3 does not.</summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>Create the bucket at startup when missing. Development convenience only.</summary>
    public bool CreateBucketIfMissing { get; set; }
}
