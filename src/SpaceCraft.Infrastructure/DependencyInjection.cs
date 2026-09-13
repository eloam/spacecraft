using Amazon.Runtime;
using Amazon.S3;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Notifications;
using SpaceCraft.Infrastructure.OpenApi;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Security;
using SpaceCraft.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SpaceCraft.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers everything the API and the worker both need: database, storage,
    /// encryption and spec validation.
    /// </summary>
    public static IServiceCollection AddSpaceCraftInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Whitespace as well as null: appsettings.json ships this key empty so the file shows
        // its shape, and "" is not null. Tested with ?? this guard could never fire.
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
        }

        services.AddDbContext<SpaceCraftDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations", "spacecraft");
                // Transient network faults are common against managed Postgres; retrying
                // here avoids failing a run for a two-second blip.
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, TimeSpan.FromSeconds(5), null);
            });

            // Whatever the layers above registered, resolved from the application's own
            // container. This keeps the direction of knowledge right: a caller can react to
            // what is being saved without persistence having to know that it exists.
            options.AddInterceptors(provider.GetServices<ISaveChangesInterceptor>());
        });

        services.AddScoped<IPipelineRunQueue, PipelineRunQueue>();

        // Registered here, not in the API: the runner raises notifications too, and a run
        // finishing is the one event neither process can see on its own.
        services.AddScoped<INotificationPublisher, NotificationPublisher>();

        // Validated at startup rather than on the first upload: a missing bucket name or a
        // blank region used to surface as whatever the S3 SDK threw, halfway through someone
        // dropping a file in.
        services.AddOptions<ObjectStorageOptions>()
            .Bind(configuration.GetSection(ObjectStorageOptions.SectionName))
            .ValidateDataAnnotations()
            // Empty keys mean the ambient credential chain, and only Development has one. On
            // the platform this deploys to a forgotten key would fail on the first S3 call,
            // long after startup reported healthy.
            .Validate(
                options => isDevelopment
                    || (!string.IsNullOrWhiteSpace(options.AccessKey)
                        && !string.IsNullOrWhiteSpace(options.SecretKey)),
                "ObjectStorage:AccessKey and ObjectStorage:SecretKey are required outside Development.")
            .ValidateOnStart();

        services.AddSingleton<IAmazonS3>(provider =>
        {
            var options = provider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<ObjectStorageOptions>>().Value;

            // Validated at startup; the null-forgiving operator asserts what ValidateOnStart established.
            var region = options.Region!;

            var config = new AmazonS3Config
            {
                ForcePathStyle = options.ForcePathStyle,
                AuthenticationRegion = region,
            };

            if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
            {
                // Scaleway Object Storage, or any other S3-compatible endpoint.
                config.ServiceURL = options.ServiceUrl;
            }
            else
            {
                config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region);
            }

            // Fall back to the ambient credential chain (IAM role, environment) when no
            // static keys are configured — static keys are a development convenience.
            return string.IsNullOrWhiteSpace(options.AccessKey)
                ? new AmazonS3Client(config)
                : new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
        });

        services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<ISpecValidator, OpenApiSpecValidator>();

        return services;
    }

    /// <summary>ASP.NET Core Identity, configured for an API rather than cookie-based MVC.</summary>
    public static IServiceCollection AddSpaceCraftIdentity(this IServiceCollection services)
    {
        services.AddIdentityCore<SpaceCraftUser>(options =>
        {
            options.User.RequireUniqueEmail = true;

            // Length beats character-class rules for real-world resistance; NIST has
            // recommended dropping composition requirements for years.
            options.Password.RequiredLength = 12;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireDigit = false;

            options.Lockout.MaxFailedAccessAttempts = 10;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
        .AddRoles<SpaceCraftRole>()
        .AddEntityFrameworkStores<SpaceCraftDbContext>()
        .AddDefaultTokenProviders();

        return services;
    }
}
