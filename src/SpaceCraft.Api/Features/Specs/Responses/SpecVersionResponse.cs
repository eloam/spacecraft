using SpaceCraft.Domain.Projects;
using SpaceCraft.Infrastructure.Storage;

namespace SpaceCraft.Api.Features.Specs.Responses;

/// <param name="Id">The version's id.</param>
/// <param name="Sequence">Its number, as the project counts uploads.</param>
/// <param name="ValidationStatus">The verdict validation reached.</param>
/// <param name="ApiTitle">info.title, when the document could be read.</param>
/// <param name="ApiVersion">info.version, when the document could be read.</param>
/// <param name="OpenApiVersion">The OpenAPI version the document declares.</param>
/// <param name="Format">"yaml" or "json" — the notation the document was uploaded in.</param>
/// <param name="InformationCount">How many findings were informational.</param>
/// <param name="WarningCount">How many were warnings.</param>
/// <param name="ErrorCount">How many were errors.</param>
/// <param name="SizeInBytes">The document's size.</param>
/// <param name="Source">How the document arrived.</param>
/// <param name="CreatedAt">When it was uploaded.</param>
/// <param name="PublishesAs">
/// What this document's version becomes at each registry, or why it would be refused.
/// Computed here rather than in the client because the rules belong to the domain and a
/// second implementation would drift from it. It also lets a target say what it will
/// publish before a run is started, which is the moment an unpublishable version is
/// cheapest to fix.
/// </param>
public sealed record SpecVersionResponse(
    Guid Id,
    int Sequence,
    SpecValidationStatus ValidationStatus,
    string? ApiTitle,
    string? ApiVersion,
    string? OpenApiVersion,
    string Format,
    int InformationCount,
    int WarningCount,
    int ErrorCount,
    long SizeInBytes,
    SpecSource Source,
    DateTimeOffset CreatedAt,
    IReadOnlyList<VersionRenderingResponse> PublishesAs)
{
    public static SpecVersionResponse From(SpecVersion s) =>
        new(s.Id,
            s.Sequence,
            s.ValidationStatus,
            s.ApiTitle,
            s.ApiVersion,
            s.OpenApiVersion,
            StorageKeys.FormatOf(s.StorageKey),
            s.InformationCount,
            s.WarningCount,
            s.ErrorCount,
            s.SizeInBytes,
            s.Source,
            s.CreatedAt,
            [.. Enum.GetValues<RegistryKind>().Select(registry =>
            {
                var rendered = RegistryVersion.Render(registry, s.ApiVersion);
                return new VersionRenderingResponse(registry, rendered.Version, rendered.Refusal);
            })]);
}
