using SpaceCraft.Api.Common;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Api.Email;

public static class EmailServiceExtensions
{
    /// <summary>
    /// Wires mail delivery: Scaleway Transactional Email when credentials are present,
    /// otherwise the log. Endpoints are unaffected either way — they see
    /// <see cref="IEmailSender"/> and the queue behind it.
    /// </summary>
    public static IServiceCollection AddSpaceCraftEmail(
        this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ScalewayEmailOptions>(configuration.GetSection(ScalewayEmailOptions.SectionName));

        var options = new ScalewayEmailOptions();
        configuration.GetSection(ScalewayEmailOptions.SectionName).Bind(options);

        if (options.IsConfigured)
        {
            services.AddHttpClient<IEmailTransport, ScalewayEmailTransport>((provider, client) =>
                {
                    var settings = provider.GetRequiredService<IOptions<ScalewayEmailOptions>>().Value;

                    client.BaseAddress = new Uri(settings.BaseUrl);

                    // The pipeline below owns timeouts. Leaving HttpClient's own timeout in
                    // place too would cut a retry short and surface as a confusing cancellation.
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    client.DefaultRequestHeaders.Add("X-Auth-Token", settings.ApiKey);
                })
                .AddStandardResilienceHandler(pipeline =>
                {
                    // Retries 5xx, 408 and 429 with exponential backoff and jitter — the
                    // jitter is what stops every queued message stampeding the provider the
                    // moment it recovers. The circuit breaker then stops hammering an outage
                    // instead of burning the queue against a dead endpoint.
                    pipeline.Retry.MaxRetryAttempts = options.MaxRetries;
                    pipeline.Retry.UseJitter = true;

                    pipeline.AttemptTimeout.Timeout = options.AttemptTimeout;
                    pipeline.TotalRequestTimeout.Timeout = options.Timeout;

                    // Must exceed the attempt timeout, and the breaker's sampling window has
                    // to be at least twice the attempt timeout or the handler refuses to build.
                    pipeline.CircuitBreaker.SamplingDuration =
                        TimeSpan.FromTicks(options.AttemptTimeout.Ticks * 2);
                });
        }
        else if (isDevelopment)
        {
            services.AddSingleton<IEmailTransport, LoggingEmailTransport>();
        }
        else
        {
            // Outside development this is a deployment mistake, not a fallback: password
            // reset links and waitlist confirmations would be written to the log store in
            // clear text, and nobody would receive them.
            throw new InvalidOperationException(
                "No email transport is configured. Set Email:Scaleway:ApiKey and "
                + "Email:Scaleway:ProjectId, or run in Development to log messages instead.");
        }

        // One queue type, three uses. Each endpoint enqueues unconditionally and a
        // dispatcher does the work, so response time never depends on what was found.
        services.AddSingleton(typeof(BackgroundWorkQueue<>));

        services.AddSingleton<IEmailSender, QueuedEmailSender>();
        services.AddHostedService<EmailDispatcher>();
        services.AddHostedService<PasswordResetDispatcher>();
        services.AddHostedService<WaitlistDispatcher>();

        return services;
    }
}
