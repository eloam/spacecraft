namespace SpaceCraft.Api.Features.Auth;

/// <summary>
/// Deliberately says nothing about whether the address is registered. The message is the
/// same either way, because a difference here is a way to test who has an account.
/// </summary>
public sealed record AcknowledgementResponse(string Message);
