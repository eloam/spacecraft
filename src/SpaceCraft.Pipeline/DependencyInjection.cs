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

        services.AddOptions<PipelineOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Steps run in this process. The boundary is the job that hosts it: one execution per
        // container, torn down afterwards, which is what the deployment provides — and which
        // holds only while a job runs a single run.
        //
        // Refused outside Development unless somebody said so in configuration. There is no
        // sandbox here, only a deployment shape that stands in for one, and a setting that
        // has to be written down is a decision taken rather than a default nobody read.
        var pipeline = configuration.GetSection(PipelineOptions.SectionName).Get<PipelineOptions>()
            ?? new PipelineOptions();

        if (!isDevelopment && !pipeline.AllowUnsandboxedExecution)
        {
            throw new InvalidOperationException(
                "Pipeline steps run in the worker's own process, with no sandbox between a "
                + "tenant's build and the host. Set Pipeline:AllowUnsandboxedExecution to true "
                + "to accept that. One tenant per process is no longer a setting to remember: "
                + "the runner executes exactly one run per invocation and has no other mode.");
        }

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
