using SpaceCraft.Api.Common;
using SpaceCraft.Api.Email;
using SpaceCraft.Api.Extensions;
using SpaceCraft.Api.Features;
using SpaceCraft.Infrastructure;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Pipeline;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;

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
builder.Services.AddSpaceCraftPipeline(builder.Configuration, builder.Environment.IsDevelopment());

builder.Services.AddScoped<SpaceCraft.Api.Features.Deletion.ResourceDeleter>();
builder.Services.AddSpaceCraftEmail(builder.Configuration, builder.Environment.IsDevelopment());

// Data Protection encrypts registry tokens at rest. The default key ring is per-machine and
// ephemeral in a container, which would make every stored token unreadable after a restart —
// so it must be persisted (and KMS-wrapped) outside Development.
builder.Services.AddDataProtection()
    .SetApplicationName("spacecraft");

builder.Services.AddProblemDetails();
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

app.UseCors(ApiServiceExtensions.SpaCorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

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
