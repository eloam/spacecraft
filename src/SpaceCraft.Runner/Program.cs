using SpaceCraft.Infrastructure;
using SpaceCraft.Infrastructure.Security;
using SpaceCraft.Infrastructure.Persistence;
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

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddSpaceCraftInfrastructure(builder.Configuration);
builder.Services.AddSpaceCraftPipeline(
    builder.Configuration,
    builder.Environment.IsDevelopment());

// The runner decrypts registry tokens, so it must resolve the same Data Protection key ring
// as the API that encrypted them. Same application name, same persisted key store.
// One call for both hosts: the API encrypts registry tokens and the runner decrypts them, so
// they have to agree about the ring and its wrapping key down to the last setting. This used
// to be the same five lines copied into each.
builder.Services.AddSpaceCraftDataProtection(
    builder.Configuration,
    builder.Environment.IsDevelopment());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RunExecutor>();

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

// A scheduler stopping the task sends SIGTERM. Cancelling lets the current run unwind; the
// lease it holds then lapses, and the next dispatch sweep sees the lapsed lease and queues the
// run again, so nothing is lost. Nothing has to be told this happened.
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
    var outcome = await executor.ExecuteAsync(command.Run, lifetime.Token);

    await host.StopAsync(CancellationToken.None);

    return (int)outcome.ExitCode();
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
