namespace SpaceCraft.Api.Features.Auth;

/// <summary>A request to email a password reset link to an address, if it has an account.</summary>
public sealed record PasswordResetRequest(string EmailAddress);
