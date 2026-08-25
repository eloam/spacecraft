using SpaceCraft.Pipeline.Building;
using SpaceCraft.Pipeline.Execution;
using SpaceCraft.Pipeline.Generation;
using SpaceCraft.Pipeline.Publishing;
using SpaceCraft.Pipeline.Sandbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Pipeline;

public static class DependencyInjection
{
    public static IServiceCollection AddSpaceCraftPipeline(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<PipelineOptions>(configuration.GetSection(PipelineOptions.SectionName));

        // Fail at startup rather than the first time a run executes untrusted code unsandboxed.
        services.AddOptions<PipelineOptions>()
            .Validate(options =>
            {
                options.Validate(isDevelopment);
                return true;
            })
            .ValidateOnStart();

        var sandbox = configuration
            .GetSection(PipelineOptions.SectionName)
            .GetValue("Sandbox", defaultValue: SandboxMode.Container);

        // PlatformIsolated and None both execute in-process; they differ in what the operator
        // is asserting about the host, which Validate enforces rather than the wiring.
        if (sandbox.UsesContainers())
        {
            services.AddSingleton<ICommandRunner, DockerCommandRunner>();
        }
        else
        {
            services.AddSingleton<ICommandRunner, LocalCommandRunner>();
        }

        services.AddSingleton<IKiotaDependencyProvider, KiotaDependencyProvider>();

        // One implementation per language/registry. Adding C# means adding a generator, a
        // scaffolder and a builder here — nothing else in the pipeline changes.
        services.AddSingleton<ISdkGenerator, KiotaSdkGenerator>();
        services.AddSingleton<IPackageScaffolder, TypeScriptPackageScaffolder>();
        services.AddSingleton<ISdkBuilder, NpmSdkBuilder>();
        services.AddSingleton<ISdkPublisher, NpmSdkPublisher>();

        services.AddScoped<IPipelineExecutor, PipelineExecutor>();

        return services;
    }
}
