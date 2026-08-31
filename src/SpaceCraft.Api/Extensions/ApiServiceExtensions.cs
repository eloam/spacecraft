using System.Text;
using System.Threading.RateLimiting;
using SpaceCraft.Api.Auth;
using SpaceCraft.Api.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

using Microsoft.EntityFrameworkCore;
using SpaceCraft.Infrastructure.Persistence;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
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

                // The two claims that made revocation mean something were issued and never
                // read. The token service writes `stamp` with the comment "invalidates
                // outstanding tokens when the user's security stamp is rotated" and `sid` so
                // "a request can tell which of the sessions it belongs to" — and nothing
                // asked. Signing out everywhere, revoking one session, and changing a
                // password all revoked refresh tokens while leaving every access token
                // already issued working until it expired on its own.
                //
                // A database read per request, which is the posture this application already
                // takes for organization membership: revocation that takes effect at the next
                // request is worth more than a claim believed for fifteen minutes.
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var principal = context.Principal;
                        if (principal is null)
                        {
                            context.Fail("No principal.");
                            return;
                        }

                        var db = context.HttpContext.RequestServices.GetRequiredService<SpaceCraftDbContext>();
                        var userId = principal.Id();

                        var stamp = await db.Users
                            .AsNoTracking()
                            .Where(u => u.Id == userId)
                            .Select(u => u.SecurityStamp)
                            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

                        if (stamp is null
                            || !string.Equals(stamp, principal.FindFirstValue("stamp"), StringComparison.Ordinal))
                        {
                            context.Fail("The security stamp has changed.");
                            return;
                        }

                        // A session is revoked by revoking its refresh tokens, so a family
                        // with none left alive is a session somebody ended.
                        if (principal.SessionId() is { } sessionId)
                        {
                            var alive = await db.RefreshTokens
                                .AsNoTracking()
                                .AnyAsync(
                                    t => t.FamilyId == sessionId && t.RevokedAt == null,
                                    context.HttpContext.RequestAborted);

                            if (!alive)
                            {
                                context.Fail("This session was revoked.");
                            }
                        }
                    },
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

    public static IServiceCollection AddSpaceCraftRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Configurable so a load test or a staging soak is not throttled into meaninglessness,
        // and so the limits can be tightened in production without a rebuild.
        var globalPermits = configuration.GetValue("RateLimits:GlobalPermitsPerMinute", 300);
        var strictPermits = configuration.GetValue("RateLimits:CredentialPermitsPerWindow", 10);
        var strictWindow = configuration.GetValue("RateLimits:CredentialWindow", TimeSpan.FromMinutes(15));

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
                        PermitLimit = globalPermits,
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
            //
            // The path is lowercased into the key because routing matches it without regard
            // to case: /api/v1/AUTH/login reaches the same endpoint and, keyed raw, was a
            // fresh budget. An attacker had as many budgets as there are ways to spell a
            // route.
            options.AddPolicy(StrictLimiter, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}"
                        + $"|{context.Request.Path.Value?.ToLowerInvariant()}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = strictPermits,
                        Window = strictWindow,
                    }));
        });

        return services;
    }
}
