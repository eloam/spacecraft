namespace SpaceCraft.Api.Features.Projects.Requests;

/// <summary>
/// What a target can still be changed to. Language and registry are not here: they decide
/// which generator runs and where the result goes, so changing either would make the row
/// describe something that was never built.
/// </summary>
public sealed record UpdateTargetRequest(
    string PackageName,
    bool AutoPublish,
    Guid? RegistryCredentialId = null);
