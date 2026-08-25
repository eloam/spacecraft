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
