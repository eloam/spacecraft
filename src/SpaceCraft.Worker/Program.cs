using SpaceCraft.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using SpaceCraft.Pipeline;
using SpaceCraft.Worker;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));

builder.Services.AddSpaceCraftInfrastructure(builder.Configuration);
builder.Services.AddSpaceCraftPipeline(builder.Configuration, builder.Environment.IsDevelopment());

// The worker decrypts registry tokens, so it must resolve the same Data Protection key ring
// as the API that encrypted them. Same application name, same persisted key store.
builder.Services.AddDataProtection().SetApplicationName("spacecraft");

builder.Services.AddHostedService<PipelineWorker>();
builder.Services.AddHostedService<LeaseReaper>();

var host = builder.Build();
await host.RunAsync();
