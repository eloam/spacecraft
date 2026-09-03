using System.Security.Cryptography;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Security;
using SpaceCraft.Domain.Registries;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using SpaceCraft.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Pipeline.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SpaceCraft.Runner;

/// <summary>Outcome of one invocation, mapped to a process exit code by the entry point.</summary>
public enum RunnerOutcome
{
    /// <summary>The work asked for was carried out. A run that failed on its own merits counts.</summary>
    Completed = 0,

    /// <summary>Nothing was there to do. Not an error: a trigger can always lose a race.</summary>
    NothingToDo = 1,

    /// <summary>The runner could not do its job — configuration, database, or an unhandled fault.</summary>
    Faulted = 2,
}

/// <summary>
/// Claims one run, executes it, and stops. The process is the unit of scheduling.
/// </summary>
/// <remarks>
/// One run per process is the isolation this design rests on: a container holds one tenant's
/// document, one tenant's registry token, and one tenant's generated code, and it is destroyed
/// afterwards. A process that serviced a queue would hold several tenants' secrets at once.
/// <para>
/// Note what this class does <em>not</em> decide. Whether a run failed is recorded against the
/// run in the database; whether the <em>invocation</em> failed is a separate question, answered
/// by <see cref="RunnerOutcomeExtensions.ExitCode"/>, because that is what the scheduler reads.
/// The two come apart on purpose, and the mapping is where the reasoning lives.
/// </para>
/// </remarks>
public sealed class RunExecutor(
    IServiceScopeFactory scopeFactory,
    ILogger<RunExecutor> logger)
{
    public async Task<RunnerOutcome> ExecuteAsync(Guid runId, CancellationToken ct)
    {
        var executorId = ExecutorId(slot: 0);

        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>();

        var run = await queue.TryClaimAsync(runId, executorId, ct);
        if (run is null)
        {
            // Another executor got there first, or the run was cancelled between trigger and
            // start. Both are ordinary, so this is not an error.
            logger.LogInformation("Run {RunId} was not available to claim; nothing to do", runId);
            return RunnerOutcome.NothingToDo;
        }

        var executor = scope.ServiceProvider.GetRequiredService<IPipelineExecutor>();
        await executor.ExecuteAsync(run.Id, ct);

        return RunnerOutcome.Completed;
    }

    /// <summary>
    /// Identifies the claimant in the runs table. Includes the machine so an abandoned lease
    /// can be traced back to the invocation that took it.
    /// </summary>
    private static string ExecutorId(int slot) =>
        $"runner-{Environment.MachineName}-{Environment.ProcessId}-{slot}";
}
