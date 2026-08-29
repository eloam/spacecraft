using MailKit.Net.Smtp;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using SpaceCraft.Api.Common;

namespace SpaceCraft.Api.Email;

public static class EmailServiceExtensions
{
    /// <summary>
    /// Wires mail delivery: an SMTP relay when credentials are present, otherwise the log.
    /// Endpoints are unaffected either way — they see <see cref="IEmailSender"/> and the
    /// queue behind it.
    /// </summary>
    public static IServiceCollection AddSpaceCraftEmail(
        this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // The credentials stay optional — empty is what selects the logging transport in
        // development. Everything else about a send is checked at startup.
        services.AddOptions<SmtpEmailOptions>()
            .Bind(configuration.GetSection(SmtpEmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = new SmtpEmailOptions();
        configuration.GetSection(SmtpEmailOptions.SectionName).Bind(options);

        if (options.IsConfigured)
        {
            // Same shape as the resilience handler the HTTP clients get, expressed directly
            // because there is no HttpClient to hang it off: retry with jittered backoff so a
            // recovering relay is not stampeded by the whole queue at once, and a per-attempt
            // timeout so one hung socket does not hold the total.
            services.AddKeyedSingleton<ResiliencePipeline>(
                SmtpEmailTransport.PipelineKey,
                (_, _) => new ResiliencePipelineBuilder()
                    .AddTimeout(new TimeoutStrategyOptions { Timeout = options.Timeout })
                    .AddRetry(new RetryStrategyOptions
                    {
                        MaxRetryAttempts = options.MaxRetries,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        // A rejected recipient or a refused password will be rejected again;
                        // only transport-level trouble is worth a second attempt.
                        ShouldHandle = new PredicateBuilder()
                            .Handle<IOException>()
                            .Handle<SmtpProtocolException>()
                            .Handle<TimeoutRejectedException>()
                            .Handle<SmtpCommandException>(IsTransient),
                    })
                    .AddTimeout(new TimeoutStrategyOptions { Timeout = options.AttemptTimeout })
                    .Build());

            services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
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
                "No email transport is configured. Set Email:Smtp:Host, Username and Password, "
                + "or run in Development to log messages instead.");
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

    /// <summary>
    /// 4xx is the relay saying "not now" — greylisting, a rate limit, a full mailbox. 5xx is
    /// it saying "not ever", and retrying a permanent rejection only spends the budget that a
    /// real outage would have needed.
    /// </summary>
    private static bool IsTransient(SmtpCommandException exception) =>
        (int)exception.StatusCode is >= 400 and < 500;
}
