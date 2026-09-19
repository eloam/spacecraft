namespace SpaceCraft.Api.Features.Specs.Responses;

/// <summary>
/// A page of versions and how many exist. Without the total, a project past the cap would be
/// shown its hundred most recent as though that were all of them.
/// </summary>
public sealed record SpecListResponse(IReadOnlyList<SpecVersionResponse> Items, int Total);
