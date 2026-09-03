using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;

// A stand-in for Scaleway Serverless Jobs, the way the neighbouring fake stands in for Key
// Manager.
//
// It exists so the code that runs in production runs here too. Until there was a trigger, the
// API wrote a queue row and compose ran the runner in a permanent --drain loop — so the local
// stack proved the pipeline and never the thing that starts it, which is precisely how a
// missing trigger survived being documented twice as existing.
//
// Unlike the key manager fake, this one does something: it starts a real container from the
// image compose built, one per run, torn down after. That is the property worth reproducing —
// not gVisor, which cannot be had locally, but one tenant per container. Spawning a child
// process instead would have meant baking the runner's whole toolchain into this image and
// keeping two images in step, which is the drift this repository already spends enough effort
// fighting.
//
// The cost is a Docker socket, which is root on the host for whoever holds it. Acceptable for
// a development service that is our own code; written down here rather than discovered.
//
// The application calls exactly one route, /start. The reads below exist for the end-to-end
// tests, so a spec can assert that a run was triggered with the right arguments without
// grepping container logs. Nothing in src/ depends on them.

var builder = WebApplication.CreateBuilder(args);

var expectedToken = builder.Configuration["Fake:ExpectedToken"] ?? "fake-token";

// Scaleway's real ceiling is 10 parallel executions on an account with only a payment method,
// 50 once identity is verified. Reproduced so a backlog meets the same refusal here that it
// will meet there — the retry that leaves a row queued is the only genuinely new logic on the
// trigger path, and it is worth exercising before production does it first.
var maxParallel = builder.Configuration.GetValue<int?>("Fake:MaxParallel") ?? 10;

var definitions = builder.Configuration.GetSection("Fake:Definitions")
    .GetChildren()
    .ToDictionary(
        section => section.Key,
        section => new JobDefinition(
            section["Image"] ?? "",
            section["Network"],
            // Flattened back to the double-underscore form a process expects. GetChildren
            // would stop at the first level and hand the runner "ConnectionStrings=" instead
            // of "ConnectionStrings__Postgres=…", which starts a container that quietly falls
            // back to its own defaults — the hardest kind of wrong to see.
            section.GetSection("Env").AsEnumerable(makePathsRelative: true)
                .Where(e => e.Value is not null && e.Key.Length > 0)
                .ToDictionary(e => e.Key.Replace(":", "__", StringComparison.Ordinal), e => e.Value!)),
        StringComparer.OrdinalIgnoreCase);

var runs = new ConcurrentDictionary<string, JobRun>(StringComparer.Ordinal);
var containers = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

// Armed by a test, consumed by the next start. Deterministic on purpose: a random failure rate
// would make the suite flaky, and the point is to exercise one specific branch on demand.
var nextStartFails = 0;

var app = builder.Build();
var log = app.Logger;

// The real API answers 401 without a valid X-Auth-Token. Enforced here so a client that
// forgets the header — or sends "Bearer <token>", which Scaleway does not use — fails on a
// developer machine instead of in production.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/serverless-jobs"))
    {
        var token = context.Request.Headers["X-Auth-Token"].ToString();
        if (!string.Equals(token, expectedToken, StringComparison.Ordinal))
        {
            await Results.Json(
                new { message = "denied authentication", type = "denied_authentication" },
                statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(context);
            return;
        }
    }

    await next(context);
});

const string Base = "/serverless-jobs/v1alpha2/regions/{region}";

app.MapPost($"{Base}/job-definitions/{{definitionId}}/start",
    async (string region, string definitionId, StartRequest? request) =>
    {
        if (!definitions.TryGetValue(definitionId, out var definition))
        {
            return Results.Json(
                new { message = $"job definition {definitionId} is not found", type = "not_found" },
                statusCode: StatusCodes.Status404NotFound);
        }

        if (Interlocked.Exchange(ref nextStartFails, 0) == 1)
        {
            return Results.Json(
                new { message = "internal server error", type = "internal_server_error" },
                statusCode: StatusCodes.Status500InternalServerError);
        }

        if (containers.Count >= maxParallel)
        {
            // 403 with a quota type is what the real API returns; the caller must read this as
            // "later", not "never", and leave its row queued.
            return Results.Json(
                new
                {
                    message = $"quota exceeded: {maxParallel} parallel job runs",
                    type = "quota_exceeded",
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var id = Guid.NewGuid().ToString();
        var args = request?.Args ?? [];
        var environment = Merge(definition.Env, request?.EnvironmentVariables);

        var run = new JobRun(id, definitionId, "running", args, environment);
        runs[id] = run;

        var name = $"spacecraft-job-{id[..8]}";
        containers[name] = 0;

        _ = Task.Run(async () =>
        {
            try
            {
                var exit = await RunContainerAsync(definition, environment, args, name, log);
                runs[id] = run with { State = exit == 0 ? "succeeded" : "failed", ExitCode = exit };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Job run {JobRunId} could not be started", id);
                runs[id] = run with { State = "failed", ErrorMessage = ex.Message };
            }
            finally
            {
                containers.TryRemove(name, out _);
            }
        });

        return Results.Json(run);
    });

app.MapGet($"{Base}/job-runs/{{id}}", (string region, string id) =>
    runs.TryGetValue(id, out var run) ? Results.Json(run) : Results.Json(
        new { message = $"job run {id} is not found", type = "not_found" },
        statusCode: StatusCodes.Status404NotFound));

app.MapGet($"{Base}/job-runs", (string region) =>
    Results.Json(new { job_runs = runs.Values.ToArray(), total_count = runs.Count }));

// Arms exactly one failure. Outside the /serverless-jobs prefix, so it needs no token and can
// never be mistaken for part of the API being imitated.
app.MapPost("/_fake/next-start-fails", () =>
{
    Interlocked.Exchange(ref nextStartFails, 1);
    return Results.Ok(new { armed = true });
});

// Lets compose wait for readiness the way it does for MinIO and Verdaccio.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// A container outlives this process unless it is stopped: --rm removes it when it exits, not
// when we die. Leaving them would quietly fill a developer's machine with half-finished builds.
app.Lifetime.ApplicationStopping.Register(() =>
{
    foreach (var name in containers.Keys)
    {
        try
        {
            Process.Start(new ProcessStartInfo("docker", $"kill {name}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Could not stop {Container}", name);
        }
    }
});

app.Run();

static Dictionary<string, string> Merge(
    IReadOnlyDictionary<string, string> definition,
    IReadOnlyDictionary<string, string>? execution)
{
    var merged = new Dictionary<string, string>(definition, StringComparer.Ordinal);

    // Scaleway's rule: on collision, the execution's value wins.
    foreach (var (key, value) in execution ?? new Dictionary<string, string>())
    {
        merged[key] = value;
    }

    return merged;
}

static async Task<int> RunContainerAsync(
    JobDefinition definition,
    IReadOnlyDictionary<string, string> environment,
    string[] args,
    string name,
    ILogger log)
{
    var start = new ProcessStartInfo("docker")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };

    start.ArgumentList.Add("run");
    start.ArgumentList.Add("--rm");
    start.ArgumentList.Add("--name");
    start.ArgumentList.Add(name);

    if (!string.IsNullOrWhiteSpace(definition.Network))
    {
        start.ArgumentList.Add("--network");
        start.ArgumentList.Add(definition.Network);
    }

    foreach (var (key, value) in environment)
    {
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add($"{key}={value}");
    }

    start.ArgumentList.Add(definition.Image);
    foreach (var argument in args)
    {
        start.ArgumentList.Add(argument);
    }

    using var process = Process.Start(start)
        ?? throw new InvalidOperationException("docker could not be started.");

    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    var output = (await stdout) + (await stderr);

    // The one failure worth naming: the image has not been built. Docker's own message for it
    // is easy to miss in a wall of output, and it costs an hour the first time.
    if (process.ExitCode != 0 && output.Contains("Unable to find image", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"The image '{definition.Image}' does not exist. Run "
            + "'docker compose -f compose.development.yaml build runner' first.");
    }

    log.LogInformation("Job container {Container} exited {ExitCode}. {Output}", name, process.ExitCode, output);
    return process.ExitCode;
}

internal sealed record JobDefinition(string Image, string? Network, Dictionary<string, string> Env);

internal sealed record StartRequest(
    [property: JsonPropertyName("args")] string[]? Args,
    [property: JsonPropertyName("startup_command")] string[]? StartupCommand,
    [property: JsonPropertyName("environment_variables")] Dictionary<string, string>? EnvironmentVariables,
    [property: JsonPropertyName("replicas")] uint? Replicas);

internal sealed record JobRun(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("job_definition_id")] string JobDefinitionId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("args")] string[] Args,
    [property: JsonPropertyName("environment_variables")] Dictionary<string, string> EnvironmentVariables,
    [property: JsonPropertyName("exit_code")] int? ExitCode = null,
    [property: JsonPropertyName("error_message")] string? ErrorMessage = null);
