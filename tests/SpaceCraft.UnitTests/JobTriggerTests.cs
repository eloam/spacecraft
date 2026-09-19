using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;
using SpaceCraft.Api.Features.Runs;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// What the trigger makes of the platform's answer.
/// </summary>
/// <remarks>
/// This classification decides a run's fate, and the two ways of getting it wrong are both
/// bad in a way nobody would notice quickly. Read a refusal that will not fix itself — an
/// unknown job definition, a rejected key — as "later", and the run is retried every few
/// seconds for ever with nothing said to the person waiting. Read a quota refusal as a fault,
/// and a run is failed for arriving during a busy minute.
/// <para>
/// Nothing here needs a network: the outcome is a function of a status code and a body, so a
/// stub handler is the whole fixture.
/// </para>
/// </remarks>
public class JobTriggerTests
{
    private static readonly Guid Run = Guid.CreateVersion7();

    [Fact]
    public async Task A_started_execution_carries_its_id_back()
    {
        var trigger = TriggerFor(HttpStatusCode.OK, """{"id":"6f0e9f1e-3a4b-4c5d-8e9f-0a1b2c3d4e5f"}""");

        var result = await trigger.StartAsync(Run, TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(JobStartOutcome.Started);

        // Kept so a failed run can be traced to the execution that ran it, which is otherwise
        // a search through a console by timestamp.
        result.JobRunId.ShouldBe("6f0e9f1e-3a4b-4c5d-8e9f-0a1b2c3d4e5f");
    }

    [Fact]
    public async Task The_run_id_travels_as_an_argument_not_as_an_environment_variable()
    {
        string? body = null;
        var trigger = TriggerFor(HttpStatusCode.OK, "{}", capture: b => body = b);

        await trigger.StartAsync(Run, TestContext.Current.CancellationToken);

        // The runner takes --run <guid>; an execution started any other way would fall back to
        // reading the environment, which is a subtler thing to depend on.
        body.ShouldNotBeNull();
        body.ShouldContain("\"args\"");
        body.ShouldContain("--run");
        body.ShouldContain(Run.ToString());
    }

    /// <summary>
    /// Refusals that mean "later". The run stays queued and the next sweep tries again, so
    /// none of these may ever count against the attempt limit.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "slow down")]
    [InlineData(HttpStatusCode.InternalServerError, "internal error")]
    [InlineData(HttpStatusCode.BadGateway, "bad gateway")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "unavailable")]
    // The one that is not a status code: Scaleway answers 403 for the parallel-run quota, and
    // 403 is otherwise a refusal retrying will not fix. The body is what tells them apart.
    [InlineData(HttpStatusCode.Forbidden, """{"type":"quota_exceeded","message":"quota exceeded"}""")]
    public async Task A_refusal_that_will_pass_is_deferred(HttpStatusCode status, string body)
    {
        var result = await TriggerFor(status, body).StartAsync(Run, TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(JobStartOutcome.Deferred);
        result.JobRunId.ShouldBeNull();
    }

    /// <summary>
    /// Refusals that will not fix themselves. These are counted, and three of them fail the
    /// run — which is right, because a run nobody can start has to say so rather than spin.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound, """{"type":"not_found","message":"job definition is not found"}""")]
    [InlineData(HttpStatusCode.Unauthorized, """{"type":"denied_authentication"}""")]
    [InlineData(HttpStatusCode.BadRequest, """{"type":"invalid_arguments"}""")]
    public async Task A_refusal_that_needs_a_person_is_rejected(HttpStatusCode status, string body)
    {
        var result = await TriggerFor(status, body).StartAsync(Run, TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(JobStartOutcome.Rejected);

        // The platform's own words, so whoever reads the failed run can tell a bad deployment
        // from a bad document.
        result.Detail.ShouldNotBeNull().ShouldContain(((int)status).ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task An_unreachable_platform_is_deferred_rather_than_failing_the_run()
    {
        var trigger = TriggerFor(_ => throw new HttpRequestException("no route to host"));

        var result = await trigger.StartAsync(Run, TestContext.Current.CancellationToken);

        // The same shape as a 5xx: the platform will come back, and the run is still queued.
        result.Outcome.ShouldBe(JobStartOutcome.Deferred);
        result.Detail.ShouldNotBeNull().ShouldContain("no route to host");
    }

    /// <summary>
    /// The exceptions the standard resilience handler throws, not the ones a bare HttpClient
    /// would: its total timeout surfaces as <see cref="TimeoutRejectedException"/> and an open
    /// circuit as <see cref="BrokenCircuitException"/>. A filter that only named the
    /// HttpClient pair let both escape, and an escaped exception aborts the whole sweep.
    /// </summary>
    [Theory]
    [MemberData(nameof(ResilienceExceptions))]
    public async Task What_the_resilience_handler_throws_is_deferred_rather_than_failing_the_run(Exception thrown)
    {
        var trigger = TriggerFor(_ => throw thrown);

        var result = await trigger.StartAsync(Run, TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(JobStartOutcome.Deferred);
    }

    public static TheoryData<Exception> ResilienceExceptions() =>
    [
        new TimeoutRejectedException("The operation didn't complete within the allowed timeout."),
        new BrokenCircuitException("The circuit is now open and is not allowing calls."),
        // Still the shape an HttpClient-owned timeout takes, kept in case the handler is ever removed.
        new TaskCanceledException("timed out"),
    ];

    [Fact]
    public async Task Cancelling_the_sweep_is_not_a_deferral()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var trigger = TriggerFor(_ => throw new TaskCanceledException("host is shutting down"));

        // A shutdown must not be recorded as the platform refusing: the caller is going away,
        // and swallowing that would make a stopping process look like a busy one.
        await Should.ThrowAsync<TaskCanceledException>(() => trigger.StartAsync(Run, cancelled.Token));
    }

    [Fact]
    public async Task A_success_with_an_unreadable_body_still_counts_as_started()
    {
        // The response shape is Scaleway's and this API is v1alpha2 — it has renamed a field
        // once already. A job that was started must not be reported as refused because the
        // payload moved, or the sweep would start a second one for the same run.
        var result = await TriggerFor(HttpStatusCode.OK, "{}").StartAsync(Run, TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(JobStartOutcome.Started);
        result.JobRunId.ShouldBeNull();
    }

    private static ScalewayJobTrigger TriggerFor(
        HttpStatusCode status, string body, Action<string>? capture = null) =>
        TriggerFor(async request =>
        {
            if (capture is not null && request.Content is not null)
            {
                capture(await request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        });

    private static ScalewayJobTrigger TriggerFor(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var http = new HttpClient(new StubHandler(respond))
        {
            BaseAddress = new Uri("https://jobs.test/"),
        };

        var options = Options.Create(new PipelineJobOptions
        {
            BaseUrl = "https://jobs.test",
            Region = "fr-par",
            JobDefinitionId = "spacecraft-pipeline",
            SecretKey = "key",
        });

        return new ScalewayJobTrigger(http, options, NullLogger<ScalewayJobTrigger>.Instance);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return respond(request);
        }
    }
}
