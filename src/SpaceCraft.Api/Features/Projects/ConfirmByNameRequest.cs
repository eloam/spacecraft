namespace SpaceCraft.Api.Features.Projects;

/// <summary>
/// Typing the name back is the confirmation. Shared by project and target deletion so the
/// two read identically, and named for what it is rather than for either one.
/// </summary>
public sealed record ConfirmByNameRequest(string Name);
