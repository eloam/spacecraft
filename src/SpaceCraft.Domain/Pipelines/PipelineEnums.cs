namespace SpaceCraft.Domain.Pipelines;

public enum RunStatus
{
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}

/// <summary>
/// The four stages the landing page promises. They always run in this order, and
/// <see cref="StepKind.Publish"/> is the only one that ever sees a registry credential.
/// </summary>
public enum StepKind
{
    Validate = 1,
    Generate = 2,
    Build = 3,
    Publish = 4,
}

public enum StepStatus
{
    Pending = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,

    /// <summary>Deliberately not run — e.g. publish on a build-only run.</summary>
    Skipped = 5,
}

/// <summary>Why a run exists, which drives what the UI shows and what auto-publish allows.</summary>
public enum RunTrigger
{
    /// <summary>A human clicked "generate".</summary>
    Manual = 1,

    /// <summary>A GitHub push changed the watched spec.</summary>
    GitHubPush = 2,

    /// <summary>A spec was uploaded through the API.</summary>
    SpecUpload = 3,
}
