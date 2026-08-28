using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Infrastructure.Storage;

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    [Required]
    public string BucketName { get; set; } = "spacecraft";

    /// <summary>
    /// S3-compatible endpoint — Scaleway Object Storage, e.g. https://s3.fr-par.scw.cloud.
    /// Leave empty to address real AWS S3 instead.
    /// </summary>
    public string? ServiceUrl { get; set; }

    [Required]
    public string Region { get; set; } = "fr-par";

    /// <summary>
    /// Deliberately not [Required]: empty means "use the ambient credential chain", which is
    /// how a deployment on an instance with an IAM role is meant to authenticate.
    /// </summary>
    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    /// <summary>
    /// Path-style addressing. Scaleway and AWS both serve virtual-hosted style, so this stays
    /// off; it exists for an S3-compatible endpoint that needs the older form.
    /// </summary>
    public bool ForcePathStyle { get; set; }
}
