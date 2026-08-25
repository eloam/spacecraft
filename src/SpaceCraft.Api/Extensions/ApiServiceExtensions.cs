using System.Text;
using System.Threading.RateLimiting;
using SpaceCraft.Api.Auth;
using SpaceCraft.Api.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

namespace SpaceCraft.Api.Extensions;

public static class ApiServiceExtensions
{
    public const string SpaCorsPolicy = "spa";

    /// <summary>Named limiter for endpoints that are cheap to call and expensive to abuse.</summary>
    public const string StrictLimiter = "strict";

    public static IServiceCollection AddSpaceCraftAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IOrganizationAccess, OrganizationAccess>();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("The Jwt configuration section is missing.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    // Tokens are short-lived; the default five-minute grace would double
                    // their effective lifetime.
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddSpaceCraftCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy(SpaCorsPolicy, policy =>
        {
            // No AllowAnyOrigin: credentials are sent on these requests, and the two are
            // mutually exclusive for good reason.
            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }));

        return services;
    }

    public static IServiceCollection AddSpaceCraftRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Baseline for authenticated traffic, partitioned per user so one tenant cannot
            // exhaust another's budget.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.Identity?.IsAuthenticated == true
                        ? context.User.Id().ToString()
                        : context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 300,
                        Window = TimeSpan.FromMinutes(1),
                    }));

            // Unauthenticated credential endpoints — login, registration, password reset,
            // waitlist signup: per-IP and deliberately tight. Identity's per-account lockout
            // stops brute force against one account; this is what stops the same weak
            // password being sprayed across many.
            // Partitioned per endpoint as well as per IP. Sharing one budget across every
            // credential endpoint sounds stricter, but it punishes the ordinary case: two
            // failed sign-ins followed by a password reset would exhaust it, and an office
            // behind one NAT address exhausts it faster still. Each endpoint gets its own
            // window; spraying one of them still hits the wall just as hard.
            options.AddPolicy(StrictLimiter, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}|{context.Request.Path}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(15),
                    }));
        });

        return services;
    }
}
