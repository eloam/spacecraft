namespace SpaceCraft.Api.Features.Runs.Requests;

public sealed record CreateRunRequest(Guid SpecVersionId, Guid SdkTargetId, bool Publish);
