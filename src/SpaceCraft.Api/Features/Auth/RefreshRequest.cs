using SpaceCraft.Domain.Identity;

namespace SpaceCraft.Api.Features.Auth;

public sealed record RefreshRequest(string RefreshToken);
