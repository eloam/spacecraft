using SpaceCraft.Api.Email;
using SpaceCraft.Api.Extensions;
using SpaceCraft.Api.Features.Account;
using SpaceCraft.Api.Features.Auth;
using SpaceCraft.Api.Features.Credentials;
using SpaceCraft.Api.Features.Members;
using SpaceCraft.Api.Features.Notifications;
using SpaceCraft.Api.Features.Organizations;
using SpaceCraft.Api.Features.Projects;
using SpaceCraft.Api.Features.Publications;
using SpaceCraft.Api.Features.Runs;
using SpaceCraft.Api.Features.Specs;
using SpaceCraft.Api.Features.Waitlist;
using SpaceCraft.Infrastructure;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using SpaceCraft.Infrastructure.Security;
using Microsoft.AspNetCore.HttpOverrides;

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

// Behind a gateway, Connection.RemoteIpAddress is the gateway — one address for every caller
// in the world, which is what the credential endpoints partition their rate limit on. Reading
// the forwarded address restores that partition.
//
// Only from proxies named in configuration, and off by default. Trusting the header from
// anyone is worse than not reading it: X-Forwarded-For is caller-supplied, so an attacker
// would mint a fresh login budget per request by varying it. KnownNetworks must name the
// gateway; see docs/deployment.md.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// --- Services -------------------------------------------------------------------------

builder.Services.AddSpaceCraftInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddSpaceCraftIdentity();
builder.Services.AddSpaceCraftAuth(builder.Configuration);
builder.Services.AddSpaceCraftCors();
builder.Services.AddSpaceCraftPublicUrls(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddSpaceCraftRateLimiting(builder.Configuration);

builder.Services.AddScoped<SpaceCraft.Api.Features.Deletion.ResourceDeleter>();
builder.Services.AddSpaceCraftEmail(builder.Configuration, builder.Environment.IsDevelopment());

// Bounds the notification history. The count beside the bell is the most frequent query
// in the application, and it stays cheap only while there is a limit on what it counts.
// The background half of each feature, listed here rather than inside the email
// registration where four of them used to hide: what runs in this process is a decision
// of the composition root, and a reader looking for "who sends invitations" finds it
// beside the feature's name.
builder.Services.AddHostedService<PasswordResetDispatcher>();
builder.Services.AddHostedService<InvitationDispatcher>();
builder.Services.AddHostedService<WaitlistDispatcher>();
builder.Services.AddHostedService<NotificationDispatcher>();
builder.Services.AddHostedService<NotificationRetention>();

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

// First, before anything reads the scheme or the address: HSTS and the HTTPS redirect below
// decide on Request.Scheme, and with a TLS-terminating gateway in front that scheme is http
// until this middleware has rewritten it from X-Forwarded-Proto. Running it later meant every
// request behind the gateway looked like plain HTTP to the redirect. With no known network
// configured the middleware trusts nothing and rewrites nothing, so it is safe to run
// unconditionally.
app.UseForwardedHeaders();

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

// Serves the email logo. Mail clients fetch images over plain HTTP from the recipient's
// machine, with no session and no ability to follow a redirect to a login page, so these
// assets have to be anonymous and static — which is all wwwroot contains.
app.UseStaticFiles();

app.UseRouting();
app.UseRequestTimeouts();
app.UseCors(ApiServiceExtensions.SpaCorsPolicy);
app.UseAuthentication();
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
