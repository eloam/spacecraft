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
using Microsoft.Extensions.Options;

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
    IOptions<RunnerOptions> options,
    TimeProvider time,
    ILogger<RunExecutor> logger)
{
    private readonly RunnerOptions _options = options.Value;

    public async Task<RunnerOutcome> ExecuteAsync(RunnerCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Mode switch
        {
            RunnerMode.OneRun => await ExecuteSingleAsync(command.RunId!.Value, ct),
            RunnerMode.Drain => await DrainAsync(ct),
            RunnerMode.Reap => await ReapAsync(ct),
            RunnerMode.Migrate => await MigrateAsync(ct),
            RunnerMode.RelocateStorage => await RelocateStorageAsync(command.DryRun, ct),
            RunnerMode.RewrapCredentials => await RewrapCredentialsAsync(command.DryRun, ct),
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

    private async Task<RunnerOutcome> DrainAsync(CancellationToken ct)
    {
        var concurrency = Math.Max(1, _options.Concurrency);
        var budget = new DrainBudget(_options.MaxRunsPerInvocation, _options.MaxDuration, time);

        logger.LogInformation(
            "Draining the queue: concurrency {Concurrency}, at most {MaxRuns} run(s), budget {Budget}",
            concurrency, _options.MaxRunsPerInvocation, _options.MaxDuration);

        await Task.WhenAll(Enumerable
            .Range(0, concurrency)
            .Select(slot => DrainSlotAsync(slot, budget, ct)));

        logger.LogInformation(
            "Drain finished after {Count} run(s) in {Elapsed}", budget.Executed, budget.Elapsed);

        return budget.Executed > 0 ? RunnerOutcome.Completed : RunnerOutcome.NothingToDo;
    }

    private async Task DrainSlotAsync(int slot, DrainBudget budget, CancellationToken ct)
    {
        var executorId = ExecutorId(slot);
        var graceRemaining = _options.EmptyQueueGrace;

        while (!ct.IsCancellationRequested)
        {
            // Reserve capacity before claiming, so concurrent slots cannot collectively
            // overrun the budget by one run each.
            if (!budget.TryReserve(out var reason))
            {
                logger.LogInformation("Executor {ExecutorId} stopping: {Reason}", executorId, reason);
                return;
            }

            // A scope per run: a DbContext must not be shared across concurrent runs, and a
            // long build should not pin a connection for the whole pipeline.
            await using var scope = scopeFactory.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>();

            var run = await queue.TryClaimAsync(executorId, ct);

            if (run is null)
            {
                budget.Release();

                if (graceRemaining <= TimeSpan.Zero)
                {
                    return;
                }

                // Give a just-committed run a moment to appear, rather than exiting on a race
                // between the trigger firing and the transaction that queued the run.
                await Task.Delay(graceRemaining, time, ct);
                graceRemaining = TimeSpan.Zero;
                continue;
            }

            graceRemaining = _options.EmptyQueueGrace;

            var executor = scope.ServiceProvider.GetRequiredService<IPipelineExecutor>();
            await executor.ExecuteAsync(run.Id, ct);
        }
    }

    /// <summary>
    /// Shared ceiling on how much one drain invocation may do. Reservations are taken before
    /// a claim and handed back when the queue turns out to be empty, so the count tracks work
    /// actually performed.
    /// </summary>
    private sealed class DrainBudget(int maxRuns, TimeSpan maxDuration, TimeProvider time)
    {
        private readonly long _startedAt = time.GetTimestamp();
        private int _reserved;

        public int Executed => Volatile.Read(ref _reserved);

        public TimeSpan Elapsed => time.GetElapsedTime(_startedAt);

        public bool TryReserve(out string reason)
        {
            if (Elapsed >= maxDuration)
            {
                reason = "time budget spent";
                return false;
            }

            if (Interlocked.Increment(ref _reserved) > maxRuns)
            {
                Interlocked.Decrement(ref _reserved);
                reason = "run budget spent";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public void Release() => Interlocked.Decrement(ref _reserved);
    }

    private async Task<RunnerOutcome> ReapAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IPipelineRunQueue>();

        var requeued = await queue.RequeueExpiredAsync(ct);

        if (requeued == 0)
        {
            logger.LogInformation("No abandoned runs to recover");
            return RunnerOutcome.NothingToDo;
        }

        logger.LogWarning("Recovered {Count} abandoned run(s)", requeued);
        return RunnerOutcome.Completed;
    }

    /// <summary>
    /// Applies pending migrations, as a deployment step rather than at application startup.
    /// <para>
    /// Startup migration is convenient with one instance and dangerous with several: two
    /// containers coming up together race on the same migration. Running it as its own job,
    /// to completion, before the new revision is rolled out, removes the race entirely.
    /// </para>
    /// </summary>
    /// <summary>
    /// Moves every stored object to the key layout <see cref="StorageKeys"/> produces today.
    ///
    /// Ordered so a crash cannot lose an object: copy, confirm the copy, rewrite the row,
    /// and only then delete the source. Interrupted anywhere, re-running finishes the job —
    /// a row whose key already matches is skipped, so the pass is idempotent.
    ///
    /// A source that is not there is reported and stepped over rather than failing the run.
    /// That is the ordinary case for a database pointing at a bucket it no longer uses.
    /// </summary>
    /// <summary>
    /// Re-encrypts every registry credential under a freshly wrapped key, then removes the
    /// unwrapped keys that used to hold them.
    /// </summary>
    /// <remarks>
    /// Switching key protection on wraps keys minted from that moment; it does nothing for
    /// the key already in the ring, which stays in clear until it expires — ninety days
    /// during which every credential written is encrypted under a key sitting in the same
    /// database. This is the pass that closes that, and it is the reason the operation exists
    /// as a command rather than a setting: it rewrites secrets and deletes a key, which is
    /// not something to discover happening at startup.
    /// <para>
    /// The order matters. Everything is re-encrypted and read back before a single old key is
    /// deleted, so a failure halfway leaves the ring able to open both shapes rather than
    /// neither.
    /// </para>
    /// </remarks>
    private async Task<RunnerOutcome> RewrapCredentialsAsync(bool dryRun, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        var keyManager = scope.ServiceProvider.GetRequiredService<IKeyManager>();
        var protection = scope.ServiceProvider.GetRequiredService<IOptions<KeyProtectionOptions>>().Value;

        if (protection.Provider is KeyProtectionProvider.None)
        {
            // Rewriting now would encrypt every credential under another key stored in clear
            // beside it — the same hole, freshly dug.
            logger.LogError(
                "Key protection is not configured, so this would re-encrypt every credential "
                + "under another unwrapped key. Set KeyProtection:Provider first");
            return RunnerOutcome.Faulted;
        }

        var unwrapped = await db.DataProtectionKeys
            .AsNoTracking()
            .Where(key => !key.Xml!.Contains("encryptedKey"))
            .Select(key => key.Id)
            .ToListAsync(ct);

        var credentials = await db.RegistryCredentials.ToListAsync(ct);

        // Decrypted before anything is written, and before a key is minted. A credential can
        // be beyond recovery — encrypted under a key ring that no longer exists, from before
        // the ring was persisted at all — and finding that out after minting a key would
        // leave an unused key behind on every attempt.
        var readable = new List<(RegistryCredential Credential, RegistrySecret Secret)>();
        var lost = new List<RegistryCredential>();

        foreach (var credential in credentials)
        {
            try
            {
                readable.Add((credential, RegistrySecret.Parse(protector.Unprotect(credential.CipherText))));
            }
            catch (CryptographicException)
            {
                lost.Add(credential);
            }
        }

        foreach (var credential in lost)
        {
            // Named rather than counted, and at warning rather than in a total: each of these
            // is a credential that can never publish again, and someone has to replace it.
            logger.LogWarning(
                "Credential {CredentialId} ({Label}, {Registry}) cannot be decrypted by any key "
                + "in the ring and is beyond recovery; it is left as it is and must be replaced",
                credential.Id, credential.Label, credential.Registry);
        }

        logger.LogInformation(
            "{Readable} credential(s) to re-encrypt, {Lost} beyond recovery; "
            + "{Unwrapped} unwrapped key(s) to remove afterwards",
            readable.Count, lost.Count, unwrapped.Count);

        if (dryRun)
        {
            logger.LogInformation("Dry run: nothing was re-encrypted and no key was removed");
            return RunnerOutcome.Completed;
        }

        if (readable.Count == 0 && unwrapped.Count == 0)
        {
            logger.LogInformation("Nothing to do: the ring holds no unwrapped key");
            return RunnerOutcome.NothingToDo;
        }

        // Minted only now that every credential is known to be readable, so a pass that
        // cannot finish leaves no key behind.
        var now = DateTimeOffset.UtcNow;
        var replacement = keyManager.CreateNewKey(now, now.AddDays(90));
        logger.LogInformation("Minted wrapped key {KeyId}", replacement.KeyId);

        foreach (var (credential, secret) in readable)
        {
            // Re-serialised rather than copied: a credential written before the document
            // shape holds a bare token, and this is where it becomes a document.
            credential.Rewrap(protector.Protect(secret.ToJson()));
        }

        await db.SaveChangesAsync(ct);

        // Read back before deleting anything. An old key removed while a credential still
        // needed it would be unrecoverable, and the failure would surface at the next publish.
        foreach (var (credential, _) in readable)
        {
            try
            {
                RegistrySecret.Parse(protector.Unprotect(credential.CipherText));
            }
            catch (CryptographicException ex)
            {
                logger.LogError(
                    ex,
                    "Credential {CredentialId} did not read back after re-encryption; no key removed",
                    credential.Id);
                return RunnerOutcome.Faulted;
            }
        }

        if (unwrapped.Count > 0)
        {
            await db.DataProtectionKeys
                .Where(key => unwrapped.Contains(key.Id))
                .ExecuteDeleteAsync(ct);
        }

        logger.LogInformation(
            "Re-encrypted {Readable} credential(s) under {KeyId} and removed {Unwrapped} unwrapped key(s)",
            readable.Count, replacement.KeyId, unwrapped.Count);

        return RunnerOutcome.Completed;
    }

    private async Task<RunnerOutcome> RelocateStorageAsync(bool dryRun, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();

        if (dryRun)
        {
            logger.LogInformation("Dry run: no object will be copied, deleted or re-keyed");
        }

        var moved = 0;
        var skipped = 0;
        var missing = 0;

        async Task<bool> MoveAsync(string from, string to)
        {
            if (string.Equals(from, to, StringComparison.Ordinal))
            {
                skipped++;
                return false;
            }

            logger.LogInformation("{Arrow} {From} -> {To}", dryRun ? "would move" : "moving", from, to);

            if (dryRun) { moved++; return false; }

            try
            {
                await storage.CopyAsync(from, to, ct);
            }
            catch (ObjectNotFoundException)
            {
                // The row outlived its object. Re-keying it would point at nothing either
                // way, so leave the row alone and let the operator see the count.
                logger.LogWarning("Source object {Key} is missing; leaving the row untouched", from);
                missing++;
                return false;
            }

            if (!await storage.ExistsAsync(to, ct))
            {
                throw new InvalidOperationException(
                    $"Copy of '{from}' to '{to}' reported success but the destination is absent.");
            }

            moved++;
            return true;
        }

        // Specs, joined to their project because the new key needs the organization too.
        var specs = await db.SpecVersions
            .Join(db.ApiProjects,
                spec => spec.ApiProjectId,
                project => project.Id,
                (spec, project) => new { spec, project })
            .ToListAsync(ct);

        var deletions = new List<string>();

        foreach (var row in specs)
        {
            var extension = Path.GetExtension(row.spec.StorageKey) is { Length: > 0 } ext ? ext : ".yaml";

            // No file name to rebuild from: it was never persisted, and the old key does not
            // carry it. StorageKeys falls back to "spec".
            var target = StorageKeys.Spec(
                row.project.OrganizationId, row.project.Id, row.spec.Id, fileName: null!, extension);

            if (await MoveAsync(row.spec.StorageKey, target))
            {
                deletions.Add(row.spec.StorageKey);
                row.spec.RelocateTo(target);
            }
        }

        var runs = await db.PipelineRuns.Include(run => run.Steps).ToListAsync(ct);

        foreach (var run in runs)
        {
            if (run.ArtifactStorageKey is { Length: > 0 } artifact)
            {
                var target = StorageKeys.Artifact(
                    run.OrganizationId, run.ApiProjectId, run.Id, Path.GetFileName(artifact));

                if (await MoveAsync(artifact, target))
                {
                    deletions.Add(artifact);
                    run.RelocateArtifactTo(target);
                }
            }

            foreach (var step in run.Steps.Where(s => s.LogStorageKey is { Length: > 0 }))
            {
                var target = StorageKeys.StepLog(
                    run.OrganizationId, run.ApiProjectId, run.Id, step.Kind.ToString());

                if (await MoveAsync(step.LogStorageKey!, target))
                {
                    deletions.Add(step.LogStorageKey!);
                    step.RelocateLogTo(target);
                }
            }
        }

        if (!dryRun)
        {
            // Saved before any delete: an interrupted pass then leaves an orphan at the old
            // key, which costs storage. The other order loses the object.
            await db.SaveChangesAsync(ct);

            foreach (var key in deletions)
            {
                try
                {
                    await storage.DeleteAsync(key, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The copy is committed and the row points at it, so the move succeeded.
                    // A stale source is worth a line, not a failure.
                    logger.LogWarning(ex, "Could not delete the old object {Key}", key);
                }
            }
        }

        logger.LogInformation(
            "Storage relocation finished: {Moved} moved, {Skipped} already in place, {Missing} missing",
            moved, skipped, missing);

        return moved == 0 ? RunnerOutcome.NothingToDo : RunnerOutcome.Completed;
    }

    private async Task<RunnerOutcome> MigrateAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("Database is already up to date");
            return RunnerOutcome.NothingToDo;
        }

        logger.LogInformation("Applying {Count} migration(s): {Migrations}",
            pending.Count, string.Join(", ", pending));

        await db.Database.MigrateAsync(ct);

        logger.LogInformation("Migrations applied");
        return RunnerOutcome.Completed;
    }

    /// <summary>
    /// Identifies the claimant in the runs table. Includes the machine so an abandoned lease
    /// can be traced back to the invocation that took it.
    /// </summary>
    private static string ExecutorId(int slot) =>
        $"runner-{Environment.MachineName}-{Environment.ProcessId}-{slot}";
}
