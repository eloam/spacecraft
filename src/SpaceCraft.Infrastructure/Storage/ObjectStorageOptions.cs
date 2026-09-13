using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Infrastructure.Storage;

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    // No initializer, on purpose. [Required] on a string that carries a default never fires
    // for a missing key — the default satisfies it — so the attribute would say "required" and
    // the property would quietly mean "spacecraft", a bucket that exists in no environment.
    [Required]
    public string? BucketName { get; set; }

    /// <summary>
    /// S3-compatible endpoint — Scaleway Object Storage, e.g. https://s3.fr-par.scw.cloud.
    /// Leave empty to address real AWS S3 instead.
    /// </summary>
    public string? ServiceUrl { get; set; }

    [Required]
    public string? Region { get; set; }

    /// <summary>
    /// Not [Required] here, because empty has a meaning: the ambient credential chain, which is
    /// how a process on an AWS instance with an IAM role authenticates. That meaning only exists
    /// in Development, though — nothing this platform deploys to has an ambient chain — so the
    /// registration requires both keys outside it.
    /// </summary>
    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    /// <summary>
    /// Path-style addressing. Scaleway and AWS both serve virtual-hosted style, so this stays
    /// off; it exists for an S3-compatible endpoint that needs the older form.
    /// </summary>
    public bool ForcePathStyle { get; set; }
}
