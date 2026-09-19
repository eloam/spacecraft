using SpaceCraft.Api.Common;

namespace SpaceCraft.Api.Features.Auth;

public sealed record UserResponse(Guid Id, string Email, string? DisplayName);
