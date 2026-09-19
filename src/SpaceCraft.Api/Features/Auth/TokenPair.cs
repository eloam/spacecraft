
namespace SpaceCraft.Api.Features.Auth;

public sealed record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);
