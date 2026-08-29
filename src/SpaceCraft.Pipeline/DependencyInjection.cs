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
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<PipelineOptions>(configuration.GetSection(PipelineOptions.SectionName));

        services.AddOptions<PipelineOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Steps run in this process. The boundary is the job that hosts it: one execution per
        // container, torn down afterwards, which is what the deployment provides.
        services.AddSingleton<ICommandRunner, LocalCommandRunner>();

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
