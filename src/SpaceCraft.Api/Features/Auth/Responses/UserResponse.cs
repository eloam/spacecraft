namespace SpaceCraft.Api.Features.Auth.Responses;

public sealed record UserResponse(Guid Id, string Email, string? DisplayName);
