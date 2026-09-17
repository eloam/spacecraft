using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using SpaceCraft.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SpaceCraft.Api.Auth;

public sealed record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

public interface ITokenService
{
    /// <summary>
    /// Issues an access/refresh pair. <paramref name="sessionId"/> is the refresh token family
    /// the pair belongs to, carried as the <c>sid</c> claim so a request can tell which of the
    /// user's sessions made it — that is what lets "sign out everywhere else" spare the caller.
    /// </summary>
    TokenPair Issue(SpaceCraftUser user, Guid sessionId);

    /// <summary>SHA-256 of a bearer-style token, for storage. Raw tokens are never persisted.</summary>
    string HashToken(string token);
}

public sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public TokenPair Issue(SpaceCraftUser user, Guid sessionId)
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
            GenerateRefreshToken(),
            expiresAt);
    }

    public string HashToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    private static string GenerateRefreshToken() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
}
