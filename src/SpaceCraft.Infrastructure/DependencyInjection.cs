using Amazon.Runtime;
using Amazon.S3;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.OpenApi;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Security;
using SpaceCraft.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        services.AddDbContext<SpaceCraftDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations", "spacecraft");
                // Transient network faults are common against managed Postgres; retrying
                // here avoids failing a run for a two-second blip.
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, TimeSpan.FromSeconds(5), null);
            });
        });

        services.AddScoped<IPipelineRunQueue, PipelineRunQueue>();

        services.Configure<ObjectStorageOptions>(configuration.GetSection(ObjectStorageOptions.SectionName));

        services.AddSingleton<IAmazonS3>(provider =>
        {
            var options = provider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<ObjectStorageOptions>>().Value;

            var config = new AmazonS3Config
            {
                ForcePathStyle = options.ForcePathStyle,
                AuthenticationRegion = options.Region,
            };

            if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
            {
                // MinIO / R2 / any S3-compatible endpoint.
                config.ServiceURL = options.ServiceUrl;
            }
            else
            {
                config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region);
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
