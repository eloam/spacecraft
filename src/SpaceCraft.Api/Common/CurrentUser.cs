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

    public static string? Email(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(JwtRegisteredClaimNames.Email) ?? principal.FindFirstValue(ClaimTypes.Email);
}
