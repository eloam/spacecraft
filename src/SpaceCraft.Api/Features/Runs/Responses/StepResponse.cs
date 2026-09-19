using SpaceCraft.Domain.Pipelines;

namespace SpaceCraft.Api.Features.Runs.Responses;

public sealed record StepResponse(
    StepKind Kind,
    StepStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    double? DurationSeconds,
    string? FailureReason,
    bool HasLog);
