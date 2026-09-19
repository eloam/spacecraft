using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpaceCraft.Domain.Notifications;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Pipelines;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Notifications;
using SpaceCraft.Infrastructure.OpenApi;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Security;
using SpaceCraft.Infrastructure.Storage;
using SpaceCraft.Pipeline;
using SpaceCraft.Pipeline.Building;
using SpaceCraft.Pipeline.Execution;
using SpaceCraft.Pipeline.Generation;
using SpaceCraft.Pipeline.Publishing;
using Shouldly;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// The executor driven through every step, against the API's own database and storage.
/// </summary>
/// <remarks>
/// Every other test that drives <see cref="PipelineExecutor"/> stops at Validate on purpose,
/// so what happens after it — the lease renewed before each step, the artifact stored, the
/// credential resolved and its address checked, the publication row written — was covered by
/// nothing but a manual run. The steps themselves are the real npm and Kiota and belong to
/// their own tests; here each is a fake that records what it was asked and answers what it
/// is told, which is what lets the run reach the end without a toolchain.
/// <para>
/// A real Postgres and a real MinIO because that is where the executor's claims land: the
/// lease on the run, the log and artifact objects, the upsert on publications. The
/// concurrency token on the run is <c>RunConcurrencyTests</c>' subject, not this suite's.
/// </para>
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class PipelineExecutionTests(SpaceCraftApi api)
{
    private const string Spec = """
        openapi: 3.0.3
        info:
          title: Billing API
          version: 1.0.0
        paths: {}
        """;

    private const string Token = "npm_test_token_1234";

    private static readonly SpecValidationResult Valid = new(
        SpecValidationStatus.Valid, "Billing API", "1.0.0", "3.0.3", [], OperationCount: 0);

    [Fact]
    public async Task A_run_goes_through_every_step_and_records_what_each_produced()
    {
        var seed = await SeedRunningRunAsync();
        var leases = new LeaseProbe(api, seed.RunId);
        var pipeline = new RecordingPipeline
        {
            Generator = new RecordingGenerator(leases.RecordAsync),
            Builder = new RecordingBuilder(leases.RecordAsync),
            Publisher = new RecordingPublisher(
                new PublishOutcome
                {
                    Kind = PublishResultKind.Succeeded,
                    Output = "+ billing-sdk@1.0.0",
                    RegistryUrl = "https://registry.npmjs.org/billing-sdk/-/billing-sdk-1.0.0.tgz",
                },
                leases.RecordAsync),
        };

        await ExecuteAsync(seed.RunId, pipeline);

        var run = await ReadRunAsync(seed.RunId);
        run.Status.ShouldBe(RunStatus.Succeeded);
        run.ResolvedVersion.ShouldBe("1.0.0");
        run.OrderedSteps().Select(s => s.Status).ShouldAllBe(s => s == StepStatus.Succeeded);

        // The document was handed over under the name the workspace gives it, extension kept.
        pipeline.Validator.FileNames.ShouldBe(["openapi.yaml"]);

        // Each step was handed the version validation resolved, and the paths it was to work
        // in are relative to the workspace, which is what keeps a command line free of host
        // paths.
        var generated = pipeline.Generator.Requests.ShouldHaveSingleItem();
        generated.Version.ShouldBe("1.0.0");
        generated.PackageName.ShouldBe("billing-sdk");
        Path.IsPathRooted(generated.SpecFilePath).ShouldBeFalse();

        pipeline.Scaffolder.Requests.ShouldHaveSingleItem().Version.ShouldBe("1.0.0");
        pipeline.Builder.Requests.ShouldHaveSingleItem().Version.ShouldBe("1.0.0");

        // The artifact outlives the workspace: stored under the run's key, and the key on the
        // run, so it can be downloaded after the directory is gone.
        run.ArtifactStorageKey.ShouldNotBeNull();
        (await ExistsInStorageAsync(run.ArtifactStorageKey)).ShouldBeTrue();

        // The publisher got the token of the credential the target names now, the address
        // from the run's own snapshot, and the file name the build step recorded on the run
        // rather than a second listing of the workspace. Here the snapshot and the credential
        // agree on the address; the drift case below is where they are told apart.
        var published = pipeline.Publisher.Requests.ShouldHaveSingleItem();
        published.Token.ShouldBe(Token);
        published.RegistryUrl.ShouldBe("https://registry.npmjs.org");
        published.ArtifactFileName.ShouldBe(RecordingBuilder.ArtifactFileName);

        // The lease is renewed before Generate, Build and Publish. Each fake read the row
        // from a second context while it ran, and noted when. The first reading is later
        // than the lease the worker was claimed with — the claim is dated in the past for
        // exactly that reason. Each reading after it is a full lease past the moment the
        // previous fake ran, which only a renewal made after that fake can explain: were the
        // renewal before Build or Publish dropped, its reading would still be the one Generate
        // got, and that one is too early.
        leases.Observed.Count.ShouldBe(3);
        var (generate, build, publish) = (leases.Observed[0], leases.Observed[1], leases.Observed[2]);
        generate.Lease.ShouldNotBeNull().ShouldBeGreaterThan(seed.InitialLease);
        build.Lease.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(generate.ReadAt + PipelineRun.LeaseDuration);
        publish.Lease.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(build.ReadAt + PipelineRun.LeaseDuration);

        // One publication row per (target, version), written before the publish and settled
        // after it, pointing at the run that did the work.
        var publication = await ReadPublicationAsync(seed.TargetId, "1.0.0");
        publication.ShouldNotBeNull();
        publication.Status.ShouldBe(PublicationStatus.Succeeded);
        publication.PipelineRunId.ShouldBe(seed.RunId);
        publication.RegistryUrl.ShouldBe("https://registry.npmjs.org/billing-sdk/-/billing-sdk-1.0.0.tgz");

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
            var target = await db.SdkTargets.SingleAsync(t => t.Id == seed.TargetId, TestContext.Current.CancellationToken);
            target.LastPublishedVersion.ShouldBe("1.0.0");

            var credential = await db.RegistryCredentials.SingleAsync(
                c => c.Id == seed.CredentialId, TestContext.Current.CancellationToken);
            credential.LastUsedAt.ShouldNotBeNull();
        }

        pipeline.Notifications.Published.ShouldHaveSingleItem().Title.ShouldStartWith("Run succeeded");
    }

    [Fact]
    public async Task A_version_published_again_reuses_its_publication_row()
    {
        var seed = await SeedRunningRunAsync();
        await ExecuteAsync(seed.RunId, new RecordingPipeline());

        // The same version, a second run: a registry holds one artifact per version, so the
        // registry says it is already there and the row says which run last tried.
        var again = await SeedAnotherRunningRunAsync(seed);
        await ExecuteAsync(again, new RecordingPipeline
        {
            Publisher = new RecordingPublisher(
                new PublishOutcome { Kind = PublishResultKind.AlreadyExists, Output = "EPUBLISHCONFLICT" }),
        });

        (await ReadRunAsync(again)).Status.ShouldBe(RunStatus.Succeeded);

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var rows = await db.Publications
            .Where(p => p.SdkTargetId == seed.TargetId && p.Version == "1.0.0")
            .ToListAsync(TestContext.Current.CancellationToken);

        var publication = rows.ShouldHaveSingleItem();
        publication.Status.ShouldBe(PublicationStatus.AlreadyExists);
        publication.PipelineRunId.ShouldBe(again);
    }

    [Fact]
    public async Task A_run_cancelled_while_generating_ends_cancelled_without_building_or_publishing()
    {
        var seed = await SeedRunningRunAsync();
        var pipeline = new RecordingPipeline
        {
            // What a cancel from the API looks like from the executor's side: another context
            // writes the row while the step is under way.
            Generator = new RecordingGenerator(ct => CancelFromAnotherScopeAsync(seed.RunId, ct)),
        };

        await ExecuteAsync(seed.RunId, pipeline);

        var run = await ReadRunAsync(seed.RunId);
        run.Status.ShouldBe(RunStatus.Cancelled);
        run.FailureReason.ShouldBe("Cancelled while generating.");

        // The step under way was left to finish; the ones after it never started. That is the
        // property that matters, because the next one after Build is a publish. It is the
        // property this pins and not the mechanism: the executor looks for a cancel between
        // steps, and had it not, the first write of Build would have tripped the concurrency
        // token and stopped the run just the same — from the builder's side the two are
        // indistinguishable, and either is what a cancel is owed.
        pipeline.Builder.Requests.ShouldBeEmpty();
        pipeline.Publisher.Requests.ShouldBeEmpty();
        run.Step(StepKind.Build).Status.ShouldBe(StepStatus.Skipped);
        run.Step(StepKind.Publish).Status.ShouldBe(StepStatus.Skipped);
        (await ReadPublicationAsync(seed.TargetId, "1.0.0")).ShouldBeNull();

        // Somebody pressed the button; telling them what they did is noise.
        pipeline.Notifications.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_credential_that_moved_since_the_run_was_queued_makes_the_publish_refuse()
    {
        var seed = await SeedRunningRunAsync();

        // The run recorded the public registry when it was queued. Meanwhile the target was
        // pointed at a credential for a private mirror — the only way a target's address moves,
        // since a credential's own address is fixed at creation.
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();

            var mirror = new RegistryCredential(
                seed.OrganizationId, RegistryKind.Npm, "mirror",
                protector.Protect(new RegistrySecret("mirror-token").ToJson()), "oken",
                "https://npm.example.test");
            db.RegistryCredentials.Add(mirror);

            var target = await db.SdkTargets.SingleAsync(t => t.Id == seed.TargetId, TestContext.Current.CancellationToken);
            target.UseCredential(mirror.Id);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var pipeline = new RecordingPipeline();
        await ExecuteAsync(seed.RunId, pipeline);

        var run = await ReadRunAsync(seed.RunId);
        run.Status.ShouldBe(RunStatus.Failed);
        run.Step(StepKind.Publish).Status.ShouldBe(StepStatus.Failed);

        // Refused rather than sent to either address, and the reason names both so that
        // somebody can decide.
        run.FailureReason.ShouldNotBeNull();
        run.FailureReason.ShouldContain("queued to publish to https://registry.npmjs.org");
        run.FailureReason.ShouldContain("now points at https://npm.example.test");

        pipeline.Publisher.Requests.ShouldBeEmpty();
        (await ReadPublicationAsync(seed.TargetId, "1.0.0")).ShouldBeNull();

        var told = pipeline.Notifications.Published.ShouldHaveSingleItem();
        told.Title.ShouldStartWith("Run failed");
        told.Detail.ShouldNotBeNull().ShouldContain("Publish step failed");
    }

    /// <summary>
    /// The real executor over the API's own database and storage, with the four steps and the
    /// notification replaced by fakes that record what they were asked.
    /// </summary>
    private async Task ExecuteAsync(Guid runId, RecordingPipeline pipeline)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var executor = new PipelineExecutor(
            services.GetRequiredService<SpaceCraftDbContext>(),
            services.GetRequiredService<IObjectStorage>(),
            pipeline.Validator,
            [pipeline.Generator],
            [pipeline.Scaffolder],
            [pipeline.Builder],
            [pipeline.Publisher],
            services.GetRequiredService<ISecretProtector>(),
            pipeline.Notifications,
            Options.Create(new PipelineOptions()),
            NullLogger<PipelineExecutor>.Instance);

        await executor.ExecuteAsync(runId, TestContext.Current.CancellationToken);
    }

    private async Task CancelFromAnotherScopeAsync(Guid runId, CancellationToken ct)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var run = await db.PipelineRuns.Include(r => r.Steps).SingleAsync(r => r.Id == runId, ct);

        run.Cancel("Cancelled while generating.");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A run a worker holds, against a real organization, project, target, credential and
    /// spec version, with the document in storage so the executor can download it.
    /// </summary>
    /// <remarks>
    /// Claimed ten minutes ago rather than now, so that a lease renewed by a step is later
    /// than the one the claim wrote — a renewal at the same instant would be invisible.
    /// </remarks>
    private async Task<Seed> SeedRunningRunAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();

        var slug = $"exec-{Guid.CreateVersion7():N}"[..24];
        var organization = new Organization("Exec", slug, Guid.CreateVersion7());
        var project = organization.AddProject("Billing", "billing");
        var target = project.AddTarget(SdkLanguage.TypeScript, "billing-sdk", RegistryKind.Npm);
        var spec = project.AddSpecVersion(id => $"specs/{id}.yaml", "hash", Spec.Length, SpecSource.Upload);

        var credential = new RegistryCredential(
            organization.Id, RegistryKind.Npm, "npm", protector.Protect(new RegistrySecret(Token).ToJson()), "1234");
        target.UseCredential(credential.Id);

        db.Organizations.Add(organization);
        db.RegistryCredentials.Add(credential);

        var run = new PipelineRun(
            organization.Id, project.Id, spec.Id, spec.Sequence, target,
            credential.RegistryUrl, credential.Label, RunTrigger.Manual, publishRequested: true,
            triggeredByUserId: Guid.CreateVersion7());

        var claimedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10);
        run.Claim("worker-1", claimedAt);

        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(ct);

        await scope.ServiceProvider.GetRequiredService<IObjectStorage>()
            .PutTextAsync(spec.StorageKey, Spec, "application/yaml", ct);

        return new Seed(
            run.Id, organization.Id, project.Id, spec.Id, target.Id, credential.Id,
            claimedAt + PipelineRun.LeaseDuration);
    }

    /// <summary>A second held run for the same target and document.</summary>
    private async Task<Guid> SeedAnotherRunningRunAsync(Seed seed)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var target = await db.SdkTargets.SingleAsync(t => t.Id == seed.TargetId, ct);
        var spec = await db.SpecVersions.SingleAsync(s => s.Id == seed.SpecVersionId, ct);
        var credential = await db.RegistryCredentials.SingleAsync(c => c.Id == seed.CredentialId, ct);

        var run = new PipelineRun(
            seed.OrganizationId, seed.ProjectId, spec.Id, spec.Sequence, target,
            credential.RegistryUrl, credential.Label, RunTrigger.Manual, publishRequested: true,
            triggeredByUserId: Guid.CreateVersion7());

        run.Claim("worker-2", DateTimeOffset.UtcNow);

        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(ct);

        return run.Id;
    }

    private async Task<PipelineRun> ReadRunAsync(Guid runId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        return await db.PipelineRuns
            .AsNoTracking()
            .Include(r => r.Steps)
            .SingleAsync(r => r.Id == runId, TestContext.Current.CancellationToken);
    }

    private async Task<Publication?> ReadPublicationAsync(Guid targetId, string version)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        return await db.Publications
            .AsNoTracking()
            .SingleOrDefaultAsync(
                p => p.SdkTargetId == targetId && p.Version == version, TestContext.Current.CancellationToken);
    }

    private async Task<bool> ExistsInStorageAsync(string key)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IObjectStorage>()
            .ExistsAsync(key, TestContext.Current.CancellationToken);
    }

    private sealed record Seed(
        Guid RunId,
        Guid OrganizationId,
        Guid ProjectId,
        Guid SpecVersionId,
        Guid TargetId,
        Guid CredentialId,
        DateTimeOffset InitialLease);

    /// <summary>
    /// Reads the run's lease from a context of its own, the way the sweep that recovers dead
    /// workers would see it, and notes when the reading was taken.
    /// </summary>
    private sealed class LeaseProbe(SpaceCraftApi api, Guid runId)
    {
        public List<(DateTimeOffset? Lease, DateTimeOffset ReadAt)> Observed { get; } = [];

        public async Task RecordAsync(CancellationToken ct)
        {
            // Taken before the query rather than after: a renewal the executor makes once
            // this fake has returned is later than this instant, whichever way the query
            // round trip falls.
            var readAt = DateTimeOffset.UtcNow;

            await using var scope = api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

            var lease = await db.PipelineRuns
                .AsNoTracking()
                .Where(r => r.Id == runId)
                .Select(r => r.LeaseExpiresAt)
                .SingleAsync(ct);

            Observed.Add((lease, readAt));
        }
    }

    /// <summary>
    /// The four steps and the notification, each a fake that records what it was asked. The
    /// defaults say yes at every step; a test replaces the one it is about.
    /// </summary>
    private sealed class RecordingPipeline
    {
        public RecordingValidator Validator { get; } = new(Valid);

        public RecordingGenerator Generator { get; init; } = new();

        public RecordingScaffolder Scaffolder { get; } = new();

        public RecordingBuilder Builder { get; init; } = new();

        public RecordingPublisher Publisher { get; init; } = new(
            new PublishOutcome { Kind = PublishResultKind.Succeeded, Output = "+ billing-sdk@1.0.0" });

        public RecordingNotifications Notifications { get; } = new();
    }

    private sealed class RecordingValidator(SpecValidationResult result) : ISpecValidator
    {
        public List<string> FileNames { get; } = [];

        public Task<SpecValidationResult> ValidateAsync(
            Stream content, string fileName, bool strict, string? previousApiVersion, CancellationToken ct)
        {
            FileNames.Add(fileName);
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// Records the request and, before answering, runs whatever the test says happens in the
    /// world while the step is under way — a cancel from the API, a reading of the lease.
    /// </summary>
    private sealed class RecordingGenerator(Func<CancellationToken, Task>? meanwhile = null) : ISdkGenerator
    {
        public SdkLanguage Language => SdkLanguage.TypeScript;

        public List<GenerationRequest> Requests { get; } = [];

        public async Task<GenerationOutcome> GenerateAsync(GenerationRequest request, CancellationToken ct)
        {
            Requests.Add(request);

            if (meanwhile is not null)
            {
                await meanwhile(ct);
            }

            return new GenerationOutcome { Succeeded = true, Output = "generated", FileCount = 1 };
        }
    }

    private sealed class RecordingScaffolder : IPackageScaffolder
    {
        public SdkLanguage Language => SdkLanguage.TypeScript;

        public List<ScaffoldRequest> Requests { get; } = [];

        public Task ScaffoldAsync(ScaffoldRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Writes a stand-in artifact where the executor expects one, since the executor reads the
    /// file back to store it and would fail the step on a name that points at nothing.
    /// </summary>
    private sealed class RecordingBuilder(Func<CancellationToken, Task>? meanwhile = null) : ISdkBuilder
    {
        public const string ArtifactFileName = "billing-sdk-1.0.0.tgz";

        public SdkLanguage Language => SdkLanguage.TypeScript;

        public List<BuildRequest> Requests { get; } = [];

        public async Task<BuildOutcome> BuildAsync(BuildRequest request, CancellationToken ct)
        {
            Requests.Add(request);

            if (meanwhile is not null)
            {
                await meanwhile(ct);
            }

            var path = Path.Combine(request.WorkspaceRoot, request.ArtifactDirectory, ArtifactFileName);
            await File.WriteAllTextAsync(path, "not really a tarball", ct);

            return new BuildOutcome
            {
                Succeeded = true,
                Output = "packed",
                ArtifactFileName = ArtifactFileName,
                ArtifactMediaType = "application/gzip",
            };
        }
    }

    private sealed class RecordingPublisher(
        PublishOutcome outcome,
        Func<CancellationToken, Task>? meanwhile = null) : ISdkPublisher
    {
        public RegistryKind Registry => RegistryKind.Npm;

        public List<PublishRequest> Requests { get; } = [];

        public async Task<PublishOutcome> PublishAsync(PublishRequest request, CancellationToken ct)
        {
            Requests.Add(request);

            if (meanwhile is not null)
            {
                await meanwhile(ct);
            }

            return outcome;
        }
    }

    private sealed class RecordingNotifications : INotificationPublisher
    {
        public List<NotificationContent> Published { get; } = [];

        public Task PublishToGovernanceAsync(
            Guid organizationId, Guid? actor, NotificationContent content, CancellationToken ct)
        {
            Published.Add(content);
            return Task.CompletedTask;
        }

        public Task PublishToMemberAsync(
            Guid organizationId, Guid userId, NotificationContent content, CancellationToken ct)
        {
            Published.Add(content);
            return Task.CompletedTask;
        }

        public Task PublishToPersonAsync(
            Guid organizationId, Guid userId, NotificationContent content, CancellationToken ct)
        {
            Published.Add(content);
            return Task.CompletedTask;
        }
    }
}
