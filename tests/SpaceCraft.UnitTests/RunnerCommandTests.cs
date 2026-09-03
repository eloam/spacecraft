using SpaceCraft.Runner;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

public class RunnerCommandTests
{
    private static readonly Func<string, string?> NoEnvironment = _ => null;

    private static Func<string, string?> Environment(string key, string value) =>
        name => name == key ? value : null;

    [Fact]
    public void No_arguments_and_no_environment_is_refused_rather_than_defaulted()
    {
        var command = RunnerCommand.Parse([], NoEnvironment);

        // Draining the queue used to be the default, and it put several tenants' runs in one
        // process. An invocation that does not say which run it is for is a mistake, and the
        // message has to say what to pass instead.
        command.IsValid.ShouldBeFalse();
        command.Mode.ShouldBe(RunnerMode.None);
        command.Error.ShouldNotBeNull().ShouldContain("--run");
    }

    [Fact]
    public void A_run_id_in_the_environment_selects_that_run()
    {
        var id = Guid.CreateVersion7();

        var command = RunnerCommand.Parse([], Environment("SPACECRAFT_RUN_ID", id.ToString()));

        command.Mode.ShouldBe(RunnerMode.OneRun);
        command.RunId.ShouldBe(id);
    }

    [Fact]
    public void The_run_flag_carries_its_id()
    {
        var id = Guid.CreateVersion7();

        var command = RunnerCommand.Parse(["--run", id.ToString()], NoEnvironment);

        command.Mode.ShouldBe(RunnerMode.OneRun);
        command.RunId.ShouldBe(id);
    }


    [Fact]
    public void An_argument_wins_over_a_run_id_left_in_the_environment()
    {
        // The environment is only consulted when no argument was given, so an explicit --run
        // cannot be redirected by a variable a scheduler forgot to clear.
        var wanted = Guid.CreateVersion7();

        RunnerCommand.Parse(
            ["--run", wanted.ToString()],
            Environment("SPACECRAFT_RUN_ID", Guid.CreateVersion7().ToString()))
            .RunId.ShouldBe(wanted);
    }

    [Fact]
    public void A_run_flag_with_no_id_is_rejected()
    {
        var command = RunnerCommand.Parse(["--run"], NoEnvironment);

        command.IsValid.ShouldBeFalse();
        command.Error.ShouldNotBeNull().ShouldContain("requires a run id");
    }

    [Fact]
    public void A_malformed_run_id_is_rejected_rather_than_ignored()
    {
        // Silently draining the whole queue because an id was mistyped would run work the
        // caller never asked for.
        var command = RunnerCommand.Parse(["--run", "not-a-guid"], NoEnvironment);

        command.IsValid.ShouldBeFalse();
        command.Error.ShouldNotBeNull().ShouldContain("not a valid run id");
    }

    [Fact]
    public void A_malformed_run_id_in_the_environment_is_rejected_too()
    {
        var command = RunnerCommand.Parse([], Environment("SPACECRAFT_RUN_ID", "42"));

        command.IsValid.ShouldBeFalse();
        command.Error.ShouldNotBeNull().ShouldContain("SPACECRAFT_RUN_ID");
    }

    [Fact]
    public void An_unknown_flag_is_rejected()
    {
        var command = RunnerCommand.Parse(["--everything"], NoEnvironment);

        command.IsValid.ShouldBeFalse();
        command.Error.ShouldNotBeNull().ShouldContain("Unrecognised");
    }

    [Fact]
    public void An_empty_environment_variable_is_treated_as_absent() =>
        RunnerCommand.Parse([], Environment("SPACECRAFT_RUN_ID", "   ")).IsValid.ShouldBeFalse();
}
