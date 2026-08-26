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
    public void No_arguments_and_no_environment_drains_the_queue()
    {
        var command = RunnerCommand.Parse([], NoEnvironment);

        command.Mode.ShouldBe(RunnerMode.Drain);
        command.IsValid.ShouldBeTrue();
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
    public void An_explicit_flag_beats_the_environment()
    {
        // A scheduler may leave a stale run id in the environment of a cron invocation.
        var command = RunnerCommand.Parse(
            ["--reap"], Environment("SPACECRAFT_RUN_ID", Guid.CreateVersion7().ToString()));

        command.Mode.ShouldBe(RunnerMode.Reap);
        command.RunId.ShouldBeNull();
    }

    [Fact]
    public void The_run_flag_carries_its_id()
    {
        var id = Guid.CreateVersion7();

        var command = RunnerCommand.Parse(["--run", id.ToString()], NoEnvironment);

        command.Mode.ShouldBe(RunnerMode.OneRun);
        command.RunId.ShouldBe(id);
    }

    [Theory]
    [InlineData("--drain", RunnerMode.Drain)]
    [InlineData("--reap", RunnerMode.Reap)]
    [InlineData("--migrate", RunnerMode.Migrate)]
    public void Each_mode_has_a_flag(string flag, RunnerMode expected) =>
        RunnerCommand.Parse([flag], NoEnvironment).Mode.ShouldBe(expected);

    [Fact]
    public void Migrate_ignores_a_run_id_left_in_the_environment() =>
        // A deployment step must never be turned into a pipeline run by a stale variable.
        RunnerCommand.Parse(
            ["--migrate"], Environment("SPACECRAFT_RUN_ID", Guid.CreateVersion7().ToString()))
            .Mode.ShouldBe(RunnerMode.Migrate);

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
        RunnerCommand.Parse([], Environment("SPACECRAFT_RUN_ID", "   ")).Mode.ShouldBe(RunnerMode.Drain);
}
