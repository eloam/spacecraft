using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Api.Features.Specs.Responses;

/// <summary>One registry's reading of an info.version.</summary>
public sealed record VersionRenderingResponse(RegistryKind Registry, string? Version, string? Refusal);
