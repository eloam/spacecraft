using System.ComponentModel.DataAnnotations;

namespace SpaceCraft.Api.Jobs;

/// <summary>
/// How the API starts a pipeline job.
/// </summary>
/// <remarks>
/// This lives under <c>SpaceCraft.Api</c> rather than in Infrastructure on purpose, and the
/// placement is the security boundary. <see cref="SecretKey"/> is a control-plane credential:
/// it starts compute. Every other secret the platform holds — the KMS key, the storage keys,
/// the connection string — is a data-plane credential, and the runner needs those. It must
/// never hold this one, or a build that runs a customer's transitive npm dependencies could
/// start jobs of its own. Keeping the type out of the shared project is what makes that
/// structural rather than a rule someone has to remember.
/// </remarks>
public sealed class PipelineJobOptions : IValidatableObject
{
    public const string SectionName = "PipelineJobs";

    /// <summary>Scaleway's Serverless Jobs endpoint, or the development fake.</summary>
    [Required]
    public string? BaseUrl { get; set; }

    [Required]
    public string? Region { get; set; }

    /// <summary>
    /// The job definition to start. One definition, created once at deployment — never one per
    /// run. A definition carries the image and the platform's own settings; what varies per run
    /// travels as arguments on the execution.
    /// </summary>
    [Required]
    public string? JobDefinitionId { get; set; }

    /// <summary>API key with permission to start this job definition.</summary>
    [Required]
    public string? SecretKey { get; set; }

    /// <summary>
    /// How often queued runs are swept and started.
    /// </summary>
    /// <remarks>
    /// There is deliberately no call from the request handler. Starting the job inline would
    /// mean a run is queued and then never started whenever that one call fails — a network
    /// blip, a 5xx, the container evicted between the commit and the response — which is the
    /// same defect as having no trigger at all, only rarer and therefore harder to see. One
    /// sweep is one code path, and a start that fails is simply retried by the next.
    /// <para>
    /// The cost is latency: a run waits up to this long before its job starts. Against a build
    /// measured in minutes that is not a trade worth thinking about.
    /// </para>
    /// </remarks>
    public int SweepSeconds { get; set; } = 5;

    /// <summary>How many runs may be started per sweep.</summary>
    /// <remarks>
    /// Scaleway allows a limited number of parallel job runs — ten on an account with only a
    /// payment method, fifty once identity is verified — and rejects the rest. That rejection
    /// is handled: the row stays queued. This bound simply keeps a backlog from spending the
    /// whole sweep collecting refusals.
    /// </remarks>
    public int BatchSize { get; set; } = 5;

    /// <summary>
    /// How long a run is left alone after a job has been started for it.
    /// </summary>
    /// <remarks>
    /// A started job takes a moment to boot and claim its run, during which the row is still
    /// queued and the next sweep would start a second job for it. That is harmless — claiming
    /// filters on <c>Queued</c> under <c>FOR UPDATE SKIP LOCKED</c>, so the loser exits having
    /// done nothing — but it spends an execution against the concurrency ceiling for nothing.
    /// <para>
    /// Held in memory rather than in a column: the only cost of forgetting it is one duplicate
    /// start, which is already safe, and a schema change for that would be a poor trade.
    /// </para>
    /// </remarks>
    public int RedispatchAfterSeconds { get; set; } = 120;

    /// <summary>
    /// What the annotations cannot say: the endpoint has to be a URL, not merely present. The
    /// https rule is not here because it depends on the environment, which a class does not
    /// know; the registration adds it.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            yield return new ValidationResult(
                $"PipelineJobs:BaseUrl must be an absolute URL; it was '{BaseUrl}'.",
                [nameof(BaseUrl)]);
        }
    }
}
