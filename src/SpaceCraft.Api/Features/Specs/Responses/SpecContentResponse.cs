namespace SpaceCraft.Api.Features.Specs.Responses;

/// <summary>
/// The document as uploaded, so findings can be shown against the source they came from.
/// Served through the API rather than as a presigned URL: it saves a CORS policy on the
/// bucket, and the upload cap already bounds what this can return.
/// </summary>
public sealed record SpecContentResponse(string Content, string Format);
