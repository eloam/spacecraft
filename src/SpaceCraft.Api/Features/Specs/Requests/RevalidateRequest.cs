namespace SpaceCraft.Api.Features.Specs.Requests;

/// <summary>
/// <paramref name="Strict"/> is nullable so that omitting it means "whatever the project is
/// set to" rather than "off": a caller that just wants today's rules replayed should not have
/// to know which mode the project runs in to ask for it.
/// </summary>
public sealed record RevalidateRequest(bool? Strict);
