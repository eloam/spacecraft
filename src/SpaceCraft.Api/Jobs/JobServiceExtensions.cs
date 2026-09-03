namespace SpaceCraft.Api.Jobs;

public static class JobServiceExtensions
{
    /// <summary>
    /// Wires the trigger that turns a queued run into a running one.
    /// </summary>
    /// <remarks>
    /// Validated at startup rather than at the first run, for the same reason key protection
    /// is: a deployment missing this setting looks completely healthy — the API serves, runs
    /// queue, the page says Queued — and the only symptom is that nothing ever happens. That
    /// is the failure this whole path exists to remove, so it must not be reachable through a
    /// forgotten environment variable.
    /// </remarks>
    public static IServiceCollection AddSpaceCraftJobs(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(PipelineJobOptions.SectionName);
        var options = section.Get<PipelineJobOptions>() ?? new PipelineJobOptions();

        Validate(options, isDevelopment);

        services.Configure<PipelineJobOptions>(section);

        services.AddHttpClient<IJobTrigger, ScalewayJobTrigger>(client =>
        {
            // Trailing slash: the route is appended relative to this, and without it the last
            // path segment of the base address is replaced rather than extended.
            client.BaseAddress = new Uri(options.BaseUrl!.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("X-Auth-Token", options.SecretKey);

            // Short: a sweep that hangs on one start delays every other queued run behind it,
            // and the next sweep is a few seconds away anyway.
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHostedService<RunDispatcher>();

        return services;
    }

    private static void Validate(PipelineJobOptions options, bool isDevelopment)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl))
        {
            throw new InvalidOperationException(
                $"PipelineJobs:BaseUrl must be an absolute URL; it was '{options.BaseUrl}'.");
        }

        // The same line the key manager draws. The development fake speaks this API over plain
        // HTTP on a private network, and it starts containers on the host it runs on — which is
        // exactly what must never be reachable from a deployment.
        if (!isDevelopment && baseUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"PipelineJobs:BaseUrl must be https outside Development; it was '{options.BaseUrl}'. "
                + "The development fake in infra/SpaceCraft.FakeScalewayJobs is not a job platform.");
        }

        foreach (var (name, value) in new[]
                 {
                     ("Region", options.Region),
                     ("JobDefinitionId", options.JobDefinitionId),
                     ("SecretKey", options.SecretKey),
                 })
        {
            if (string.IsNullOrWhiteSpace(value) || value == "[REQUIRED]")
            {
                throw new InvalidOperationException(
                    $"PipelineJobs:{name} is required. Without it the API queues runs that "
                    + "nothing will ever start, and says nothing about it.");
            }
        }
    }
}
