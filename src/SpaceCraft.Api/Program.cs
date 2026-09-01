using SpaceCraft.Api.Common;
using SpaceCraft.Api.Email;
using SpaceCraft.Api.Extensions;
using SpaceCraft.Api.Features;
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

builder.Services.AddSpaceCraftInfrastructure(builder.Configuration);
builder.Services.AddSpaceCraftIdentity();
builder.Services.AddSpaceCraftAuth(builder.Configuration);
builder.Services.AddSpaceCraftCors(builder.Configuration);
builder.Services.AddSpaceCraftRateLimiting(builder.Configuration);

// Bounded, because one of its users is fed by the URL. The slug cache keys on a path segment
// the caller writes, so an unbounded cache is a memory leak anybody can drive; entries declare
// a size and the oldest go when the limit is reached.
builder.Services.AddMemoryCache(options => options.SizeLimit = 20_000);

// Empties the slug cache the moment a save moves a slug, so a renamed or freshly created
// name can never be resolved to the row that used to answer to it.
builder.Services.AddScoped<ISaveChangesInterceptor, SpaceCraft.Api.Common.SlugCacheInvalidator>();
builder.Services.AddScoped<SpaceCraft.Api.Features.Deletion.ResourceDeleter>();
builder.Services.AddSpaceCraftEmail(builder.Configuration, builder.Environment.IsDevelopment());

// Data Protection encrypts registry tokens at rest. The default key ring is per-machine and
// ephemeral in a container, which would make every stored token unreadable after a restart —
// so it must be persisted (and KMS-wrapped) outside Development.
// One call for both hosts: the API encrypts registry tokens and the runner decrypts them, so
// they have to agree about the ring and its wrapping key down to the last setting. This used
// to be the same five lines copied into each.
builder.Services.AddSpaceCraftDataProtection(
    builder.Configuration,
    builder.Environment.IsDevelopment());

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

// Authentication first, then the slug rewrite, then routing.
//
// The rewrite has to precede routing — the endpoint would already be chosen otherwise, and
// changing the path afterwards changes nothing. But it used to precede authentication too,
// which made it the first thing an anonymous request reached: every call to
// /api/v1/organizations/<anything> ran a database query and inserted an entry, keyed on a
// string the caller chose, into a cache the rate limiter sits behind and therefore cannot
// protect. It also answered differently for a slug that exists, which is a tenant directory
// for anyone who can send a request.
//
// Authentication needs no routing of its own — the scheme is a bearer token in a header —
// so it can move ahead of both, and the rewrite now runs only for a caller we know.
app.UseAuthentication();

app.UseMiddleware<SpaceCraft.Api.Common.SlugRouteMiddleware>();
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
   .MapMemberEndpoints();

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
