using SpaceCraft.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using SpaceCraft.Pipeline;
using SpaceCraft.Runner;
using System.Runtime.InteropServices;
using Serilog;

var command = RunnerCommand.Parse(args, Environment.GetEnvironmentVariable);

if (!command.IsValid)
{
    await Console.Error.WriteLineAsync(command.Error);
    await Console.Error.WriteLineAsync();
    await Console.Error.WriteLineAsync(RunnerCommand.Usage);
    return (int)RunnerExitCode.InvalidArguments;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddOptions<RunnerOptions>()
    .Bind(builder.Configuration.GetSection(RunnerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSpaceCraftInfrastructure(builder.Configuration);
builder.Services.AddSpaceCraftPipeline(builder.Configuration, builder.Environment.IsDevelopment());

// The runner decrypts registry tokens, so it must resolve the same Data Protection key ring
// as the API that encrypted them. Same application name, same persisted key store.
builder.Services.AddDataProtection().SetApplicationName("spacecraft");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RunExecutor>();

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

// A scheduler stopping the task sends SIGTERM. Cancelling lets the current run unwind; the
// lease it holds then lapses and the reaper returns it to the queue, so nothing is lost.
using var lifetime = new CancellationTokenSource();
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    logger.LogInformation("SIGTERM received; finishing the current step and exiting");
    lifetime.Cancel();
});
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    lifetime.Cancel();
};

try
{
    await host.StartAsync(lifetime.Token);

    var executor = host.Services.GetRequiredService<RunExecutor>();
    var outcome = await executor.ExecuteAsync(command, lifetime.Token);

    await host.StopAsync(CancellationToken.None);

    return (int)ExitCodeFor(outcome);
}
catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
{
    logger.LogInformation("Runner cancelled before finishing; any claimed run will be requeued");
    return (int)RunnerExitCode.Success;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Runner faulted");
    return (int)RunnerExitCode.Faulted;
}

// A run that fails on its own merits is recorded in the database and is not a failure of the
// invocation — exiting non-zero would make a scheduler retry work already accounted for.
// Only the runner being unable to do its job warrants a non-zero code.
static RunnerExitCode ExitCodeFor(RunnerOutcome outcome) => outcome switch
{
    RunnerOutcome.Completed => RunnerExitCode.Success,
    RunnerOutcome.NothingToDo => RunnerExitCode.Success,
    _ => RunnerExitCode.Faulted,
};
