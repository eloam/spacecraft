using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace SpaceCraft.Api.Common;

/// <summary>Reads the authenticated principal without scattering claim strings across endpoints.</summary>
public static class CurrentUser
{
    public static Guid Id(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("The authenticated principal carries no usable subject claim.");
    }

    /// <summary>
    /// The refresh token family this request's access token belongs to — the session. Absent
    /// on tokens issued before sessions were tracked, so callers must handle null.
    /// </summary>
    public static Guid? SessionId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sid), out var id)
            ? id
            : null;
    }

    public static string? Email(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(JwtRegisteredClaimNames.Email) ?? principal.FindFirstValue(ClaimTypes.Email);
}
