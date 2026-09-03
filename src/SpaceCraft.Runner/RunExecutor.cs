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
/// Executes pipeline runs and then stops, rather than servicing a queue forever.
/// <para>
/// This is the same work <c>SpaceCraft.Worker</c> does, shaped for an environment where the
/// process *is* the unit of scheduling — a serverless job, a batch task, a one-shot pod. The
/// long-lived worker keeps its polling loop; both call the same executor underneath, so the
/// pipeline has exactly one implementation.
/// </para>
/// <para>
/// A run that fails is a normal result, not a failure of the invocation: the run is marked
/// failed in the database and the process still exits zero. Exiting non-zero would make a
/// scheduler retry the whole job and re-execute work that is already accounted for.
/// </para>
/// </summary>
public sealed class RunExecutor(
    IServiceScopeFactory scopeFactory,
    ILogger<RunExecutor> logger)
{

    public async Task<RunnerOutcome> ExecuteAsync(RunnerCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Mode switch
        {
            RunnerMode.OneRun => await ExecuteSingleAsync(command.RunId!.Value, ct),
            _ => RunnerOutcome.Faulted,
        };
    }

    private async Task<RunnerOutcome> ExecuteSingleAsync(Guid runId, CancellationToken ct)
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
