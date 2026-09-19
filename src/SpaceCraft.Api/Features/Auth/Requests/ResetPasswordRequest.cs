namespace SpaceCraft.Api.Features.Auth.Requests;

public sealed record ResetPasswordRequest(string Token, string NewPassword);
