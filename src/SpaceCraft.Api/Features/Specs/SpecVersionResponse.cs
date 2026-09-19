using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Infrastructure.Storage;

namespace SpaceCraft.Api.Features.Specs;

public sealed record SpecVersionResponse(
    Guid Id,
    int Sequence,
    SpecValidationStatus ValidationStatus,
    string? ApiTitle,
    string? ApiVersion,
    string? OpenApiVersion,
    /// <summary>"yaml" or "json" — the notation the document was uploaded in.</summary>
    string Format,
    int InformationCount,
    int WarningCount,
    int ErrorCount,
    long SizeInBytes,
    SpecSource Source,
    DateTimeOffset CreatedAt,
    /// <summary>
    /// What this document's version becomes at each registry, or why it would be refused.
    /// </summary>
    /// <remarks>
    /// Computed here rather than in the client because the rules belong to the domain and a
    /// second implementation would drift from it. It also lets a target say what it will
    /// publish before a run is started, which is the moment an unpublishable version is
    /// cheapest to fix.
    /// </remarks>
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
