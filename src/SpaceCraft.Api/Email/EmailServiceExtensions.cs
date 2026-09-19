using MailKit.Net.Smtp;
using Polly;
using Polly.Retry;
using Polly.Timeout;

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
            // Not on the class, because the options class cannot know where it is running. A
            // relay reached in the clear is a relay on this machine; anywhere else the
            // credentials and every reset link cross the network readable.
            .Validate(
                options => isDevelopment || options.Security != SmtpSecurity.None,
                "Smtp:Security is None, which sends the password and every message "
                + "unencrypted. That is only allowed in Development — use StartTls on port "
                + "587 or ImplicitTls on 465.")
            .ValidateOnStart();

        // Read eagerly, and only for the branch: which transport exists depends on whether a
        // relay is named, and a registration that branches on configuration may read it.
        // Nothing below is built from this copy — the transport takes the validated
        // IOptions<SmtpEmailOptions>.
        var options = new SmtpEmailOptions();
        configuration.GetSection(SmtpEmailOptions.SectionName).Bind(options);

        if (options.IsConfigured)
        {
            // Same shape as the resilience handler the HTTP clients get, expressed directly
            // because there is no HttpClient to hang it off: retry with jittered backoff so a
            // recovering relay is not stampeded by the whole queue at once, and a per-attempt
            // timeout so one hung socket does not hold the total. The budgets are constants,
            // as they are for the jobs client: no environment has ever set them differently,
            // and the one relation that matters — an attempt shorter than the total — is
            // something a setting could break and a constant cannot.
            services.AddKeyedSingleton<ResiliencePipeline>(
                SmtpEmailTransport.PipelineKey,
                (_, _) => new ResiliencePipelineBuilder()
                    .AddTimeout(new TimeoutStrategyOptions { Timeout = SmtpEmailTransport.TotalTimeout })
                    .AddRetry(new RetryStrategyOptions
                    {
                        MaxRetryAttempts = SmtpEmailTransport.MaxRetries,
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
                    .AddTimeout(new TimeoutStrategyOptions { Timeout = SmtpEmailTransport.AttemptTimeout })
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
                "No email transport is configured. Set Smtp:Host, Username and Password, "
                + "or run in Development to log messages instead.");
        }

        // One open generic, so a feature can queue its own request type without this
        // registration knowing it exists. The dispatchers that drain those queues belong to
        // their features and are registered in Program.cs, beside them; what is registered
        // here is only the mail itself — the queue, the announcement, the sender.
        services.AddSingleton(typeof(BackgroundWorkQueue<>));

        services.AddHostedService<EmailTransportAnnouncement>();
        services.AddSingleton<IEmailSender, QueuedEmailSender>();
        services.AddHostedService<EmailDispatcher>();

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
