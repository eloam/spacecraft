using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpaceCraft.Infrastructure.Persistence;

namespace SpaceCraft.Infrastructure.Security;

/// <summary>
/// Data Protection, configured once for every host that needs it.
/// </summary>
/// <remarks>
/// The API encrypts registry tokens and the runner decrypts them, so the two must agree about
/// the ring down to the application name and the wrapping key. They used to hold identical
/// copies of the same five lines; if those ever drifted the failure would be a publish that
/// cannot read its own credential, discovered in production rather than by the compiler.
/// </remarks>
public static class KeyProtectionExtensions
{
    public static IServiceCollection AddSpaceCraftDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(KeyProtectionOptions.SectionName);

        services.AddOptions<KeyProtectionOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            // The whole point of the fake is that it speaks the same API over plain HTTP on a
            // private network. That is exactly what must never be reachable from production:
            // an https endpoint is the line between the two, and it is cheaper to refuse here
            // than to discover a wrapping key travelling in clear. A BaseUrl that does not
            // parse is reported by the class itself, so it passes here.
            .Validate(
                options => options.Provider is not KeyProtectionProvider.ScalewayKms
                    || isDevelopment
                    || !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl)
                    || baseUrl.Scheme == Uri.UriSchemeHttps,
                "KeyProtection:BaseUrl must be https outside Development. "
                + "The development fake in infra/SpaceCraft.FakeScalewayKms is not a key manager.")
            // Checked rather than trusted: the fake accepts any region, so a typo would work
            // all the way to production and fail there on the first credential. A blank
            // region is reported by the class itself, so it passes here.
            .Validate(
                options => options.Provider is not KeyProtectionProvider.ScalewayKms
                    || isDevelopment
                    || string.IsNullOrWhiteSpace(options.Region)
                    || KeyProtectionOptions.KnownRegions.Contains(options.Region, StringComparer.Ordinal),
                "KeyProtection:Region is not one Key Manager serves "
                + $"({string.Join(", ", KeyProtectionOptions.KnownRegions)}).")
            .ValidateOnStart();

        // Read eagerly, and only the key the branch turns on: which services exist depends on
        // the provider, and a registration that branches on configuration may read it. Nothing
        // below is built from this read — the client and the readiness check take the
        // validated IOptions<KeyProtectionOptions>.
        var provider = section.GetValue<KeyProtectionProvider>(nameof(KeyProtectionOptions.Provider));

        if (provider is KeyProtectionProvider.ScalewayKms)
        {
            AddScalewayKms(services);
        }
        else if (isDevelopment)
        {
            // Left unwrapped only here, and only because losing a development ring costs
            // nothing but locally stored test tokens.
            services.AddHostedService<UnprotectedKeyRingWarning>();
        }
        else
        {
            // The same judgement as an unencrypted SMTP relay outside Development: a
            // deployment mistake, not a fallback. Starting anyway would write the ring in
            // clear into the database it is supposed to protect, and nothing would say so.
            throw new InvalidOperationException(
                "No key protection is configured. Set KeyProtection:Provider to ScalewayKms "
                + "with BaseUrl, Region, KeyId and SecretKey, or run in "
                + "Development to store the key ring unwrapped.");
        }

        // Registered after the readiness check above so that check runs first: hosted services
        // start in registration order, and Data Protection loads the ring in one of its own.
        // A ring loaded before the KMS is known to answer is the dangerous case — a decryptor
        // that throws does not stop startup, it makes the key disappear.
        services.AddDataProtection()
            .SetApplicationName("spacecraft")
            // Shared through Postgres: the API encrypts registry tokens and the runner
            // decrypts them from another container, with no filesystem between them.
            .PersistKeysToDbContext<SpaceCraftDbContext>();

        return services;
    }

    private static void AddScalewayKms(IServiceCollection services)
    {
        services.AddHttpClient<ScalewayKmsClient>((provider, client) =>
        {
            // Resolved here rather than read at registration, so the client sees the validated
            // options and nothing has to be parsed twice.
            var kms = provider.GetRequiredService<IOptions<KeyProtectionOptions>>().Value;

            // Trailing slash: routes are appended relative to this, and without it the last
            // path segment of the base address would be replaced instead of extended.
            client.BaseAddress = new Uri(kms.BaseUrl!.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("X-Auth-Token", kms.SecretKey);
        })
            // Retries, exponential backoff with jitter, per-attempt and total timeouts, and a
            // circuit breaker. Worth more here than anywhere else in the application: the
            // readiness check unwraps every stored key before the host is allowed to serve, so
            // without this a momentary blip at the key manager is not a slow request, it is an
            // instance that fails to start.
            .AddStandardResilienceHandler();

        services.AddSingleton<ScalewayKmsXmlEncryptor>();

        // The documented seam for a custom encryptor. Resolved lazily so the client is built
        // by the container rather than here.
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(provider =>
            new ConfigureOptions<KeyManagementOptions>(keyManagement =>
                keyManagement.XmlEncryptor = provider.GetRequiredService<ScalewayKmsXmlEncryptor>()));

        services.AddHostedService<KeyProtectionReadiness>();
    }
}
