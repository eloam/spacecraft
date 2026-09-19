using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SpaceCraft.Api.Features.Runs;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Common;
using SpaceCraft.Api.Email;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Amazon.S3;
using Amazon.S3.Model;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Xunit;

namespace SpaceCraft.IntegrationTests;

/// <summary>
/// The API, its database, and nothing stubbed between them.
/// </summary>
/// <remarks>
/// These tests exist for the seams unit tests cannot reach: tenant isolation, the shape of a
/// refusal, whether a value written through an endpoint is really encrypted in the column,
/// and whether a URL still resolves to the row it named. Every one of those is a claim about
/// the whole request path — route, middleware, guard, query — and each has been wrong at
/// least once in a way no unit test would have noticed.
/// <para>
/// Postgres comes from a container rather than the development database: a suite that leaves
/// rows behind in the database someone is working in gets run less and less.
/// </para>
/// <para>
/// Object storage does too, and for a second reason. Uploading a specification is part of
/// most of what these tests exercise, and it was reaching the MinIO from docker-compose on
/// the developer's own machine — so the suite passed here and had nothing to write to on a
/// build agent, where no such thing is running. A dependency only one machine satisfies is
/// not a dependency the tests declare.
/// </para>
/// </remarks>
public sealed class SpaceCraftApi : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// Named as the deployment names it. The application never creates a bucket — doing so in
    /// production would need a permission it has no other use for — so the test that stands in
    /// for the deployment has to.
    /// </summary>
    private const string BucketName = "openapi-bucket";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("spacecraft")
        .WithUsername("spacecraft")
        .WithPassword("spacecraft")
        .Build();

    // The same image compose runs, so the tests and the development stack agree about what
    // they are talking to.
    private readonly MinioContainer _storage = new MinioBuilder("minio/minio:latest")
        .WithUsername("spacecraft")
        .WithPassword("spacecraft-test-secret")
        .Build();

    private string StorageUrl =>
        new UriBuilder(Uri.UriSchemeHttp, _storage.Hostname, _storage.GetMappedPublicPort(9000))
            .Uri.ToString();

    public async ValueTask InitializeAsync()
    {
        // Together: neither waits on the other, and starting two containers one after the
        // other is most of what a run of this suite spends its time on.
        await Task.WhenAll(_postgres.StartAsync(), _storage.StartAsync());

        await CreateBucketAsync();

        // Migrated through a context of its own, built before this factory's host exists.
        //
        // Reaching for Services here would build the host first, and the host does not sit
        // still: Data Protection reads its key ring as soon as it is asked for one, from a
        // table the migration has not created yet. Whether that read landed before or after
        // this line was a race — it usually lost, and when it won a request failed with
        // 42P01 on a table nothing in the test had mentioned.
        var options = new DbContextOptionsBuilder<SpaceCraftDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var db = new SpaceCraftDbContext(options);
        await db.Database.MigrateAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _storage.DisposeAsync();
    }

    /// <summary>
    /// Makes the bucket the application expects to find already there.
    /// </summary>
    private async Task CreateBucketAsync()
    {
        using var client = new AmazonS3Client(
            new Amazon.Runtime.BasicAWSCredentials(_storage.GetAccessKey(), _storage.GetSecretKey()),
            new AmazonS3Config
            {
                ServiceURL = StorageUrl,
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
            });

        await client.PutBucketAsync(new PutBucketRequest { BucketName = BucketName });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Development, deliberately: it is the one environment that allows an unwrapped key
        // ring, and wrapping has its own tests. Asking every integration test to stand up a
        // key manager would buy nothing and cost a container.
        builder.UseEnvironment(Environments.Development);

        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("KeyProtection:Provider", "None");
        builder.UseSetting("Database:MigrateOnStartup", "false");

        // Path style, like MinIO everywhere else: the bucket is a path segment rather than a
        // subdomain, which is the one thing that differs from Scaleway and AWS.
        builder.UseSetting("ObjectStorage:ServiceUrl", StorageUrl);
        builder.UseSetting("ObjectStorage:BucketName", BucketName);
        builder.UseSetting("ObjectStorage:AccessKey", _storage.GetAccessKey());
        builder.UseSetting("ObjectStorage:SecretKey", _storage.GetSecretKey());
        builder.UseSetting("ObjectStorage:ForcePathStyle", "true");

        // Every test registers its own account, and the credential endpoints are deliberately
        // throttled per address — which in a test host is one address for the whole suite, so
        // the tenth test would be refused for reasons that have nothing to do with what it
        // asserts. Raised rather than removed, so a runaway loop still stops.
        builder.UseSetting("RateLimits:CredentialPermitsPerWindow", "10000");
        builder.UseSetting("RateLimits:GlobalPermitsPerMinute", "100000");

        // The notification sweep is a poll, and a test that waits for a message to arrive
        // waits for it. One second rather than fifteen.
        builder.UseSetting("Notifications:SweepSeconds", "1");

        builder.ConfigureTestServices(services =>
        {
            // Replaces the development transport that writes to the log, so a test can read
            // what was sent rather than grep for it.
            services.RemoveAll<IEmailTransport>();
            services.AddSingleton<IEmailTransport>(new CapturingTransport(Mailbox));

            // Picked up by the DbContext the way the application's own interceptors are.
            services.AddSingleton<ISaveChangesInterceptor>(BeforeSaving);

            // The run dispatcher must not run here. It is a background sweep, and this host
            // loads the API's own appsettings.Development.json — which points PipelineJobs at
            // the development fake on localhost:8098. A developer with that stack up would
            // have their integration tests start real containers for runs seeded in a
            // throwaway database, and DispatchClaimTests would be racing a third sweeper it
            // never agreed to. Tests that want the claim call it directly.
            foreach (var sweep in services
                .Where(s => s.ImplementationType == typeof(RunDispatcher))
                .ToList())
            {
                services.Remove(sweep);
            }
        });
    }

    /// <summary>
    /// Every message the application tried to send, in order.
    /// </summary>
    /// <remarks>
    /// The invitation flow puts its whole grant in an email and stores only a hash, which is
    /// the point of it — so the only way to test acceptance the way a recipient experiences it
    /// is to read the mailbox. This is that mailbox.
    /// </remarks>
    public sealed class Outbox
    {
        private readonly List<EmailMessage> _sent = [];

        private readonly Dictionary<string, int> _refusals = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<EmailMessage> Sent
        {
            get { lock (_sent) { return [.. _sent]; } }
        }

        public void Add(EmailMessage message)
        {
            lock (_sent) { _sent.Add(message); }
        }

        /// <summary>
        /// Makes the next <paramref name="times"/> messages to an address fail the way a relay
        /// does, so a test can see what the application does about it.
        /// </summary>
        public void RefuseNext(string to, int times)
        {
            lock (_sent) { _refusals[to] = times; }
        }

        /// <summary>How many refusals an address still has coming.</summary>
        public int RefusalsLeft(string to)
        {
            lock (_sent) { return _refusals.GetValueOrDefault(to); }
        }

        /// <summary>Spends one refusal for the address, if any is left.</summary>
        public bool TakeRefusal(string to)
        {
            lock (_sent)
            {
                if (!_refusals.TryGetValue(to, out var left) || left == 0)
                {
                    return false;
                }

                _refusals[to] = left - 1;
                return true;
            }
        }

        /// <summary>
        /// Waits for the nth message of a kind to an address, since sending happens off the
        /// request thread.
        /// </summary>
        /// <remarks>
        /// Matched on the subject as well as the address, because registering already sends a
        /// welcome to the same mailbox — waiting on the address alone hands back whichever
        /// message happens to be there.
        /// </remarks>
        public async Task<EmailMessage> WaitForAsync(
            string to, string subjectContains, CancellationToken ct, int ordinal = 1)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var matching = Sent
                    .Where(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase)
                                && m.Subject.Contains(subjectContains, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matching.Count >= ordinal)
                {
                    return matching[ordinal - 1];
                }

                await Task.Delay(50, ct);
            }

            throw new InvalidOperationException(
                $"Message {ordinal} matching '{subjectContains}' was never sent to {to}.");
        }
    }

    private sealed class CapturingTransport(Outbox outbox) : IEmailTransport
    {
        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (outbox.TakeRefusal(message.To))
            {
                // The exception the SMTP transport lets through once its own retries are spent.
                throw new IOException("The test relay refused the message.");
            }

            outbox.Add(message);
            return Task.CompletedTask;
        }
    }

    public Outbox Mailbox { get; } = new();

    /// <summary>
    /// A place to put a second request between a handler's reads and its write.
    /// </summary>
    /// <remarks>
    /// A race is two requests interleaved at a point neither controls, and a test that sends
    /// two at once only sometimes gets the interleaving it means to prove. The seam is the
    /// save: the interceptor runs whatever the test armed inside SaveChanges, after the
    /// handler has read and judged and before its statements go out — so the write that
    /// follows is the stale one, every time.
    /// <para>
    /// Armed for one save, chosen by what it is about to write; every context in the host
    /// goes through here, background sweeps included, and the hook must not fire on theirs.
    /// Disarmed before it runs, since the hook's own save comes through here too.
    /// </para>
    /// </remarks>
    public sealed class SaveChangesHook : SaveChangesInterceptor
    {
        private (Func<DbContext, bool> When, Func<CancellationToken, Task> Run)? _armed;

        public void Arm(Func<DbContext, bool> when, Func<CancellationToken, Task> run) =>
            _armed = (when, run);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(eventData);

            if (_armed is { } armed && eventData.Context is { } context)
            {
                // Not yet run at this point of the save, as in the auditing example of the
                // interceptors documentation: without it a member removed from a loaded
                // collection is not Deleted yet, and the predicate looks at nothing.
                context.ChangeTracker.DetectChanges();

                if (armed.When(context))
                {
                    _armed = null;
                    await armed.Run(cancellationToken);
                }
            }

            return result;
        }
    }

    public SaveChangesHook BeforeSaving { get; } = new();

    /// <summary>A client already signed in as a brand-new account.</summary>
    public async Task<HttpClient> SignedInAsync(string email)
    {
        var client = CreateClient();

        var registered = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password = "Correct-Horse-Battery-9!", displayName = email.Split('@')[0] },
            TestContext.Current.CancellationToken);

        registered.EnsureSuccessStatusCode();

        var session = await registered.Content.ReadFromJsonAsync<AuthPayload>(TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        return client;
    }

    /// <summary>Only what the tests read back; the response carries more.</summary>
    private sealed record AuthPayload(string AccessToken);
}

/// <summary>
/// One API and one database for the whole suite. Registering a user per test keeps them
/// independent without paying for a container each time.
/// </summary>
[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<SpaceCraftApi>;
