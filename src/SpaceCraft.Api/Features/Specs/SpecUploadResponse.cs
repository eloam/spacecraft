using SpaceCraft.Domain.Projects;
using SpaceCraft.Infrastructure.OpenApi;

namespace SpaceCraft.Api.Features.Specs;

/// <summary>
/// <paramref name="Strict"/> reports the mode these findings were produced under, rather than
/// leaving the caller to assume the one it asked for: revalidation can be asked to run the
/// other way, and a client that reports its own request describes a run that may not have
/// happened.
/// </summary>
public sealed record SpecUploadResponse(
    SpecVersionResponse SpecVersion,
    IReadOnlyList<SpecDiagnostic> Diagnostics,
    IReadOnlyList<Guid> TriggeredRunIds,
    bool Strict);
