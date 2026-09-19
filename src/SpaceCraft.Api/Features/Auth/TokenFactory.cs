using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SpaceCraft.Api.Features.Auth;

public sealed class TokenFactory(IOptions<JwtOptions> options) : ITokenFactory
{
    private readonly JwtOptions _options = options.Value;

    public TokenPair Create(SpaceCraftUser user, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(user);

        var expiresAt = DateTimeOffset.UtcNow.Add(JwtOptions.AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            // Unique per token, so a specific token can be revoked by id later.
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            // Invalidates outstanding tokens when the user's security stamp is rotated.
            new("stamp", user.SecurityStamp ?? string.Empty),
            // The refresh token family this access token belongs to — the session.
            new(JwtRegisteredClaimNames.Sid, sessionId.ToString()),
        };

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            claims.Add(new Claim("name", user.DisplayName));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new TokenPair(
            new JwtSecurityTokenHandler().WriteToken(token),
            OpaqueTokens.Generate(),
            expiresAt);
    }
}
