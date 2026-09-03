namespace SpaceCraft.Runner;

/// <summary>
/// Process exit codes. Schedulers key their retry behaviour off these, so the distinction
/// between "the run failed" and "the runner failed" has to be visible here.
/// </summary>
public enum RunnerExitCode
{
    /// <summary>The invocation did what it was asked, including finding nothing to do.</summary>
    Success = 0,

    /// <summary>The runner could not operate: bad configuration, unreachable database, unhandled fault.</summary>
    Faulted = 1,

    /// <summary>The command line made no sense. Retrying will not help.</summary>
    InvalidArguments = 2,
}

/// <summary>
/// Turns the result of an invocation into the number the scheduler sees.
/// </summary>
/// <remarks>
/// This lives here, rather than as a local function beside the code that calls it, so that a
/// test can reach it. Local functions in top-level statements compile into a generated class
/// no test project can name, and this mapping is the runner's entire interface with whatever
/// starts it — the one piece of behaviour most worth pinning.
/// </remarks>
public static class RunnerOutcomeExtensions
{
    /// <summary>
    /// A run that fails on its own merits is recorded as failed in the database and is not a
    /// failure of the invocation: the process still exits zero. Exiting non-zero would make the
    /// scheduler retry the job and re-execute work that is already accounted for — and the run
    /// has an attempt limit, so the retry would burn through it without anyone asking for it.
    /// Only the runner being unable to do its job warrants a non-zero code.
    /// </summary>
    public static RunnerExitCode ExitCode(this RunnerOutcome outcome) => outcome switch
    {
        RunnerOutcome.Completed => RunnerExitCode.Success,

        // A duplicate dispatch is ordinary — two sweeps can start a job for one run, and the
        // second finds it claimed. Reporting that as a fault would make the scheduler retry a
        // run that is already being executed by someone else.
        RunnerOutcome.NothingToDo => RunnerExitCode.Success,

        // Deliberately a discard rather than a case per member: an outcome added later without
        // a thought for this mapping should fail loudly to the scheduler, not quietly succeed.
        _ => RunnerExitCode.Faulted,
    };
}
