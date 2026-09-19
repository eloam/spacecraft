using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Domain.Identity;

namespace SpaceCraft.Api.Features.Auth;

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    UserResponse User)
{
    public static AuthResponse From(TokenPair pair, SpaceCraftUser user) =>
        new(pair.AccessToken,
            pair.RefreshToken,
            pair.AccessTokenExpiresAt,
            new UserResponse(user.Id, user.Email ?? string.Empty, user.DisplayName));
}
