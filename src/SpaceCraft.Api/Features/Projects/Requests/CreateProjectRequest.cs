namespace SpaceCraft.Api.Features.Projects.Requests;

/// <summary>
/// <paramref name="StrictValidation"/> is nullable so that omitting it means "whatever the
/// default is" rather than "off" — a caller that predates the option should not silently
/// create projects without it.
/// </summary>
public sealed record CreateProjectRequest(string Name, string? Slug, bool? StrictValidation);
