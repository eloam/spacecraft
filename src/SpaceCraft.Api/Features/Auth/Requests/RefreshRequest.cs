using SpaceCraft.Domain.Identity;

namespace SpaceCraft.Api.Features.Auth.Requests;

public sealed record RefreshRequest(string RefreshToken);
