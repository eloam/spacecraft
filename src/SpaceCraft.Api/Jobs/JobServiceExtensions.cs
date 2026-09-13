using Microsoft.Extensions.Options;

namespace SpaceCraft.Api.Jobs;

public static class JobServiceExtensions
{
    /// <summary>
    /// Wires the trigger that turns a queued run into a running one.
    /// </summary>
    /// <remarks>
    /// Validated at startup rather than at the first run: a deployment missing this setting
    /// looks completely healthy — the API serves, runs queue, the page says Queued — and the
    /// only symptom is that nothing ever happens. That is the failure this whole path exists
    /// to remove, so it must not be reachable through a forgotten environment variable.
    /// <para>
    /// Through the options pipeline rather than a hand-written check at registration, because
    /// nothing here branches on the values — the same services are registered whatever they
    /// are — and the annotations report every failing member of this section together, where
    /// the hand-written loop stopped at the first. (The URL rule in IValidatableObject only runs
    /// once the annotations pass; that is how the validator sequences the two.) (Across sections the first failing one still
    /// stops startup; the gain is within a section, and that is where the forgotten keys are.)
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSpaceCraftJobs(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PipelineJobOptions>()
            .Bind(configuration.GetSection(PipelineJobOptions.SectionName))
            .ValidateDataAnnotations()
            // The same line the key manager draws. The development fake speaks this API over
            // plain HTTP on a private network, and it starts containers on the host it runs on
            // — which is exactly what must never be reachable from a deployment. A BaseUrl
            // that does not parse is reported by the class itself, so it passes here.
            .Validate(
                options => isDevelopment
                    || !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl)
                    || baseUrl.Scheme == Uri.UriSchemeHttps,
                "PipelineJobs:BaseUrl must be https outside Development. "
                + "The development fake in infra/SpaceCraft.FakeScalewayJobs is not a job platform.")
            .ValidateOnStart();

        services.AddHttpClient<IJobTrigger, ScalewayJobTrigger>((provider, client) =>
        {
            // Resolved here rather than read at registration, so the client sees the validated
            // options and nothing has to be parsed twice.
            var options = provider.GetRequiredService<IOptions<PipelineJobOptions>>().Value;

            // Trailing slash: the route is appended relative to this, and without it the last
            // path segment of the base address is replaced rather than extended.
            client.BaseAddress = new Uri(options.BaseUrl!.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("X-Auth-Token", options.SecretKey);

            // Infinite here on purpose: the resilience handler below owns the deadlines, and an
            // HttpClient timeout under its total budget would fire first and cut the retries
            // short — the classic way to configure both and get neither.
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
            // A transient 5xx should cost this call a few hundred milliseconds, not the whole
            // batch: the sweep stops at the first deferral and releases everything behind it,
            // so without retries one blip delays every other queued run by a sweep. Quota
            // refusals are 403 and are not retried, which is right — they mean later, and the
            // run stays queued for the next sweep either way.
            .AddStandardResilienceHandler(resilience =>
            {
                // Shorter than the default 30s: a sweep runs every few seconds, and a start
                // that is still failing after this should let the next sweep try rather than
                // hold the batch.
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
            });

        // Singleton: a Meter and its observable instruments are created once and live as long
        // as the process. Registered before the dispatcher that writes to it.
        services.AddSingleton<PipelineMetrics>();
        services.AddHostedService<RunDispatcher>();

        return services;
    }
}
