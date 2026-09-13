using SpaceCraft.Api.Common;
using SpaceCraft.Api.Email;
using SpaceCraft.Api.Extensions;
using SpaceCraft.Api.Features;
using SpaceCraft.Api.Jobs;
using SpaceCraft.Infrastructure;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SpaceCraft.Infrastructure.Security;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);
// Secrets arrive as files, not as environment variables. The deployment mounts each one at
// /run/secrets/<key>, where the file name is the configuration key with the same double
// underscores the environment uses — so ConnectionStrings__Postgres becomes
// ConnectionStrings:Postgres and the settings block in docs/deployment.md transposes unchanged.
//
// The default host does not do this. It reads appsettings, the environment and the command
// line, and a mounted file it was never told about is simply not configuration — which is what
// makes this one line load-bearing rather than decorative.
//
// Added last, so a file wins over an environment variable of the same name. The point of the
// mount is that a secret is not sitting in the process environment; letting a stray variable
// shadow it would quietly restore the thing being avoided.
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

// Serverless container platforms inject the port to listen on and route to it. Binding to
// 0.0.0.0 rather than localhost is not optional there: the startup probe connects from
// outside the container, and a loopback-only listener fails it and the deploy with it.
// ASPNETCORE_URLS still wins when set, so local runs and launch profiles are unaffected.
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port
    && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// --- Services -------------------------------------------------------------------------

builder.Services.AddSpaceCraftInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddSpaceCraftIdentity();
builder.Services.AddSpaceCraftAuth(builder.Configuration);
builder.Services.AddSpaceCraftCors(builder.Configuration);
builder.Services.AddSpaceCraftRateLimiting(builder.Configuration);

builder.Services.AddScoped<SpaceCraft.Api.Features.Deletion.ResourceDeleter>();
builder.Services.AddSpaceCraftEmail(builder.Configuration, builder.Environment.IsDevelopment());

// Bounds the notification history. The count beside the bell is the most frequent query
// in the application, and it stays cheap only while there is a limit on what it counts.
builder.Services.AddHostedService<SpaceCraft.Api.Common.NotificationRetention>();

// Data Protection encrypts registry tokens at rest. The default key ring is per-machine and
// ephemeral in a container, which would make every stored token unreadable after a restart —
// so it must be persisted (and KMS-wrapped) outside Development.
// One call for both hosts: the API encrypts registry tokens and the runner decrypts them, so
// they have to agree about the ring and its wrapping key down to the last setting. This used
// to be the same five lines copied into each.
builder.Services.AddSpaceCraftDataProtection(
    builder.Configuration,
    builder.Environment.IsDevelopment());

// Turns a queued run into a running one. Without it the API writes a row that nothing reads:
// runs sit at Queued for ever, and no page or log says so. It lives here rather than beside
// --reap in the runner image because the key that starts jobs must never reach a process that
// executes a customer's build.
builder.Services.AddSpaceCraftJobs(builder.Configuration, builder.Environment.IsDevelopment());

builder.Services.AddProblemDetails();
// Runs before the default handler, which would answer 500 to a request the caller malformed.
builder.Services.AddExceptionHandler<SpaceCraft.Api.Common.BadRequestExceptionHandler>();
builder.Services.AddRequestTimeouts();
builder.Services.AddOpenApi(options => options.AddSchemaTransformer<NumericSchemaTransformer>());

builder.Services.AddHealthChecks()
    .AddDbContextCheck<SpaceCraftDbContext>("postgres");

// Uploads are capped well below the spec size limit's worst case; the endpoint checks the
// real limit itself and returns a proper validation error.
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 12 * 1024 * 1024;
});

var app = builder.Build();

// --- HTTP pipeline --------------------------------------------------------------------

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("SpaceCraft API"));
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRequestTimeouts();
// Serves the email logo. Mail clients fetch images over plain HTTP from the recipient's
// machine, with no session and no ability to follow a redirect to a login page, so these
// assets have to be anonymous and static — which is all wwwroot contains.
app.UseStaticFiles();

// Ahead of routing, which the bearer scheme has no need of — it reads a header. What it must
// be ahead of is the rate limiter, for the reason set out where that is added.
app.UseAuthentication();

// Behind a gateway, Connection.RemoteIpAddress is the gateway — one address for every caller
// in the world, which is what the credential endpoints partition their rate limit on. Reading
// the forwarded address restores that partition.
//
// Only from proxies named in configuration, and off by default. Trusting the header from
// anyone is worse than not reading it: X-Forwarded-For is caller-supplied, so an attacker
// would mint a fresh login budget per request by varying it. KnownProxies or KnownNetworks
// must name the gateway; see docs/deployment.md.
var forwardedFrom = builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
if (forwardedFrom.Length > 0)
{
    var forwarded = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };

    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();

    foreach (var network in forwardedFrom)
    {
        var parts = network.Split('/', 2);
        forwarded.KnownIPNetworks.Add(new System.Net.IPNetwork(
            IPAddress.Parse(parts[0]),
            parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 32));
    }

    app.UseForwardedHeaders(forwarded);
}

app.UseRouting();

app.UseCors(ApiServiceExtensions.SpaCorsPolicy);
app.UseAuthorization();

// After authentication, not before it. The global limiter partitions on the caller's user id
// and falls back to their address when there is no user — and running ahead of
// UseAuthentication meant context.User was still the empty principal every time, so the
// fallback was the only branch ever taken and the per-user isolation its comment promises
// never existed: everyone behind one address shared a single budget. The cost of the new
// order is that a token is validated before the limiter sees the request, which is signature
// work, not database work; the credential endpoints that must refuse traffic before anything
// expensive keep their own per-address policy and are unaffected.
app.UseRateLimiter();

app.MapHealthChecks("/health").AllowAnonymous();

var api = app.MapGroup("/api/v1");

api.MapAuthEndpoints()
   .MapWaitlistEndpoints()
   .MapOrganizationEndpoints()
   .MapProjectEndpoints()
   .MapSpecEndpoints()
   .MapRunEndpoints()
   .MapCredentialEndpoints()
   .MapAccountEndpoints()
   .MapPublicationEndpoints()
   .MapMemberEndpoints()
   .MapNotificationEndpoints();

// Applying migrations at startup is convenient for a single-instance dev setup. In
// production this belongs in a deployment step: concurrent instances racing on the same
// migration is a good way to corrupt a schema.
if (app.Environment.IsDevelopment() &&
    app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
    await db.Database.MigrateAsync();
}

await app.RunAsync();

/// <summary>Exposed so integration tests can spin the API up with WebApplicationFactory.</summary>
public partial class Program;
