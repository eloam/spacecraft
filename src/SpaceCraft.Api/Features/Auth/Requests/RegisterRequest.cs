namespace SpaceCraft.Api.Features.Auth.Requests;

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);
