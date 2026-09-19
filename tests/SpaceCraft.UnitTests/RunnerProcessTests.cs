using System.Diagnostics;
using Xunit.Sdk;
using SpaceCraft.Runner;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The runner as the scheduler sees it: a process, an exit code, and stderr.
/// </summary>
/// <remarks>
/// Everything else about the runner is tested through its classes. This one starts the real
/// binary, because the property being tested is not a function's return value — it is that a
/// misconfigured invocation is refused <em>before</em> the host is built, and no unit test can
/// observe an ordering it does not participate in.
/// <para>
/// Cheap, despite appearances: the referenced project's output, apphost and runtimeconfig
/// included, is copied next to this assembly, and the run under test never reaches a database.
/// If it ever did, this test would be the thing that noticed.
/// </para>
/// </remarks>
public class RunnerProcessTests
{
    [Theory]
    [InlineData("--nonsense")]
    [InlineData("--run")]
    [InlineData("--run", "not-a-guid")]
    public async Task A_command_line_that_makes_no_sense_exits_two(params string[] arguments)
    {
        var (exit, _, stderr) = await RunAsync(arguments);

        // Two, specifically — not one. Two is the runner's only way of saying "retrying will
        // not help": a job definition whose args were dropped or renamed starts a container
        // that can never work, and reporting that as a fault makes a permanent misconfiguration
        // look exactly like an unreachable database.
        exit.ShouldBe((int)RunnerExitCode.InvalidArguments);

        // On stderr, where a scheduler collecting logs will keep it apart from the run's output.
        stderr.ShouldContain("--run <guid>");
    }

    [Fact]
    public async Task An_invocation_that_names_no_run_refuses_rather_than_choosing_one()
    {
        // No arguments and no SPACECRAFT_RUN_ID. There is deliberately no default: draining the
        // queue used to be one, and it put several tenants' runs in a single container.
        var (exit, _, stderr) = await RunAsync([], clearRunId: true);

        exit.ShouldBe((int)RunnerExitCode.InvalidArguments);
        stderr.ShouldContain("SPACECRAFT_RUN_ID");
    }

    [Fact]
    public async Task A_bad_command_line_is_refused_without_a_database()
    {
        // Pointed at a host that does not exist, under the key the runner actually reads —
        // ConnectionStrings:Postgres, the one AddSpaceCraftInfrastructure asks for. Parsing
        // happens before Host.CreateApplicationBuilder, so this still exits 2 — and that
        // ordering is the point. Move the parse below the host and this becomes a container
        // that waits on a connection it will never get, then dies as a fault, which is the
        // one outcome that tells the operator nothing.
        var (exit, stdout, stderr) = await RunAsync(
            ["--nonsense"],
            environment: new()
            {
                ["ConnectionStrings__Postgres"] = "Host=192.0.2.1;Port=5432;Database=nope;Username=nope;Password=nope",
            });

        exit.ShouldBe((int)RunnerExitCode.InvalidArguments);
        stderr.ShouldContain("Unrecognised argument");

        // Refused before the connection string was ever looked at: nothing the process wrote
        // names the host it was pointed at.
        (stdout + stderr).ShouldNotContain("192.0.2.1");
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> RunAsync(
        string[] arguments,
        Dictionary<string, string>? environment = null,
        bool clearRunId = false)
    {
        var runner = Path.Combine(AppContext.BaseDirectory, "SpaceCraft.Runner.dll");
        File.Exists(runner).ShouldBeTrue(
            $"The runner is referenced by this project, so its output should sit at {runner}.");

        // Launched through `dotnet` rather than the apphost so the test does not have to know
        // whether this platform names it SpaceCraft.Runner or SpaceCraft.Runner.exe.
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add(runner);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // A run id left in the developer's own environment would otherwise decide the outcome
        // of tests about its absence.
        if (clearRunId)
        {
            start.Environment.Remove("SPACECRAFT_RUN_ID");
        }

        foreach (var (key, value) in environment ?? [])
        {
            start.Environment[key] = value;
        }

        using var process = Process.Start(start).ShouldNotBeNull();

        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        // Generous, and a ceiling rather than a measurement: a hang here means the parse now
        // waits on something, which is exactly the regression this file exists to catch.
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cap.CancelAfter(TimeSpan.FromSeconds(60));

        try
        {
            await process.WaitForExitAsync(cap.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new XunitException("The runner did not exit; a bad command line should be refused at once.");
        }

        return (process.ExitCode, await stdout, await stderr);
    }
}
