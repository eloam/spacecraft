using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace SpaceCraft.Api.Features.Runs;

/// <summary>The outcome of asking the platform to start a job.</summary>
public enum JobStartOutcome
{
    /// <summary>An execution was created. <c>JobRunId</c> identifies it.</summary>
    Started,

    /// <summary>
    /// The platform refused for now — the parallel-run quota, or a 5xx. The run stays queued
    /// and the next sweep tries again.
    /// </summary>
    Deferred,

    /// <summary>
    /// The platform refused in a way retrying will not fix: a job definition that does not
    /// exist, a rejected key. Still leaves the run queued, because the fault is the
    /// deployment's and a person has to look.
    /// </summary>
    Rejected,
}

public readonly record struct JobStartResult(JobStartOutcome Outcome, string? JobRunId, string? Detail);

/// <summary>Starts one pipeline job.</summary>
/// <remarks>
/// One method, because the API needs exactly one call. It never reads an execution back:
/// the queue in Postgres already records what happened to a run, and asking the platform for
/// a second account of the same fact would only raise the question of which one is right.
/// </remarks>
public interface IJobTrigger
{
    Task<JobStartResult> StartAsync(Guid runId, CancellationToken ct = default);
}

/// <summary>
/// Starts a Serverless Job execution per pipeline run.
/// </summary>
/// <remarks>
/// The run id travels in <c>args</c>, not in the environment, for two reasons. The runner
/// already parses <c>--run &lt;guid&gt;</c>, and arguments override the image's <c>CMD</c> — so
/// an execution started this way can never fall back to draining a queue, which would put
/// several tenants' builds in one process. The environment would also work in principle
/// (the runner reads <c>SPACECRAFT_RUN_ID</c>) but only once nothing supplies a default
/// command, which is a subtler thing to depend on.
/// <para>
/// Behind an interface because this API is <c>v1alpha2</c> and has already renamed a field.
/// </para>
/// </remarks>
public sealed class ScalewayJobTrigger(
    HttpClient http,
    IOptions<PipelineJobOptions> options,
    ILogger<ScalewayJobTrigger> logger) : IJobTrigger
{
    private readonly PipelineJobOptions _options = options.Value;

    public async Task<JobStartResult> StartAsync(Guid runId, CancellationToken ct = default)
    {
        var route =
            $"serverless-jobs/v1alpha2/regions/{_options.Region}/job-definitions/{_options.JobDefinitionId}/start";

        try
        {
            using var response = await http.PostAsJsonAsync(
                route,
                new StartJobRequest(["--run", runId.ToString()]),
                ct);

            if (response.IsSuccessStatusCode)
            {
                var run = await response.Content.ReadFromJsonAsync<JobRunResponse>(ct);
                return new JobStartResult(JobStartOutcome.Started, run?.Id, null);
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            // Quota refusals are the expected steady state above the parallel-run ceiling, not
            // a fault: the backlog drains as executions finish. Separated from a genuine
            // misconfiguration so the logs do not treat them alike.
            var deferred =
                response.StatusCode is HttpStatusCode.TooManyRequests
                    or >= HttpStatusCode.InternalServerError
                || body.Contains("quota", StringComparison.OrdinalIgnoreCase);

            return new JobStartResult(
                deferred ? JobStartOutcome.Deferred : JobStartOutcome.Rejected,
                null,
                $"{(int)response.StatusCode} {response.ReasonPhrase}. {Shorten(body)}");
        }
        catch (Exception ex) when (
            ex is HttpRequestException
                or TaskCanceledException
                or TimeoutRejectedException
                or BrokenCircuitException
            && !ct.IsCancellationRequested)
        {
            // Unreachable is the same shape as a 5xx: leave it queued, try again shortly. The
            // last two are what the standard resilience handler throws when its total timeout
            // expires or its circuit is open — the pipeline's own words for "not now", and
            // letting them escape would abort the sweep instead of deferring the run.
            logger.LogWarning(ex, "Could not reach the jobs API to start run {RunId}", runId);
            return new JobStartResult(JobStartOutcome.Deferred, null, ex.Message);
        }
    }

    private static string Shorten(string body) =>
        body.Length <= 300 ? body : body[..300] + "…";

    private sealed record StartJobRequest(
        [property: JsonPropertyName("args")] string[] Args);

    private sealed record JobRunResponse(
        [property: JsonPropertyName("id")] string? Id);
}
