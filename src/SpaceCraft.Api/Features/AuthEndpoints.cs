using System.Security.Claims;
using SpaceCraft.Api.Auth;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Api.Features;

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    UserResponse User);

public sealed record UserResponse(Guid Id, string Email, string? DisplayName);

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Auth");

        // Response types are declared explicitly so they reach the OpenAPI document.
        // Minimal API handlers returning IResult are opaque to the schema generator, and an
        // API whose own contract is under-specified is a poor advertisement for a product
        // that generates SDKs from OpenAPI.
        group.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .Produces<AuthResponse>()
            .ProducesValidationProblem()
            .WithSummary("Creates an account and returns a token pair.");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithSummary("Exchanges credentials for a token pair.");

        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithSummary("Rotates a refresh token for a new token pair.");

        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Revokes the caller's refresh token family.");

        group.MapGet("/me", GetMeAsync)
            .RequireAuthorization()
            .Produces<UserResponse>()
            .WithSummary("Returns the authenticated user.");

        return api;
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        UserManager<SpaceCraftUser> userManager,
        SpaceCraftDbContext db,
        ITokenService tokens,
        IOptions<JwtOptions> jwtOptions,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return ApiResults.Invalid(nameof(request.Email), "An email address is required.");
        }

        var user = new SpaceCraftUser
        {
            UserName = request.Email.Trim().ToLowerInvariant(),
            Email = request.Email.Trim().ToLowerInvariant(),
            DisplayName = request.DisplayName?.Trim(),
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return TypedResults.ValidationProblem(result.Errors
                .GroupBy(e => MapErrorField(e.Code))
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal));
        }

        var pair = await IssueAndStoreAsync(user, db, tokens, jwtOptions.Value, familyId: null, ct);
        return TypedResults.Ok(ToResponse(pair, user));
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        UserManager<SpaceCraftUser> userManager,
        SpaceCraftDbContext db,
        ITokenService tokens,
        IOptions<JwtOptions> jwtOptions,
        CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(request.Email ?? string.Empty);

        // Same response whether the account is missing or the password is wrong, so the
        // endpoint cannot be used to enumerate registered addresses.
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password ?? string.Empty))
        {
            return ApiResults.Problem(
                "Invalid credentials",
                "The email address or password is incorrect.",
                StatusCodes.Status401Unauthorized);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return ApiResults.Problem(
                "Account locked",
                "Too many failed attempts. Try again later.",
                StatusCodes.Status423Locked);
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        var pair = await IssueAndStoreAsync(user, db, tokens, jwtOptions.Value, familyId: null, ct);
        return TypedResults.Ok(ToResponse(pair, user));
    }

    private static async Task<IResult> RefreshAsync(
        [FromBody] RefreshRequest request,
        SpaceCraftDbContext db,
        UserManager<SpaceCraftUser> userManager,
        ITokenService tokens,
        IOptions<JwtOptions> jwtOptions,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return ApiResults.Invalid(nameof(request.RefreshToken), "A refresh token is required.");
        }

        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        var now = DateTimeOffset.UtcNow;

        if (stored is null)
        {
            return ApiResults.Problem("Invalid token", "The refresh token is not recognised.", StatusCodes.Status401Unauthorized);
        }

        // A token presented twice means the first holder's copy leaked. Revoking the whole
        // family logs out the attacker and the legitimate user, which is the safe outcome.
        if (stored.UsedAt is not null)
        {
            var family = await db.RefreshTokens.Where(t => t.FamilyId == stored.FamilyId).ToListAsync(ct);
            foreach (var token in family)
            {
                token.Revoke(now);
            }

            await db.SaveChangesAsync(ct);

            return ApiResults.Problem(
                "Token reuse detected",
                "This refresh token has already been used. All sessions in its family have been revoked.",
                StatusCodes.Status401Unauthorized);
        }

        if (!stored.IsActive(now))
        {
            return ApiResults.Problem("Expired token", "The refresh token has expired or been revoked.", StatusCodes.Status401Unauthorized);
        }

        var user = await userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null)
        {
            return ApiResults.Problem("Invalid token", "The refresh token is not recognised.", StatusCodes.Status401Unauthorized);
        }

        stored.MarkUsed(now);

        var pair = await IssueAndStoreAsync(user, db, tokens, jwtOptions.Value, stored.FamilyId, ct);
        return TypedResults.Ok(ToResponse(pair, user));
    }

    private static async Task<IResult> LogoutAsync(
        [FromBody] RefreshRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        ITokenService tokens,
        CancellationToken ct)
    {
        var userId = principal.Id();
        var hash = tokens.HashRefreshToken(request.RefreshToken ?? string.Empty);

        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == userId, ct);

        if (stored is not null)
        {
            var now = DateTimeOffset.UtcNow;
            var family = await db.RefreshTokens.Where(t => t.FamilyId == stored.FamilyId).ToListAsync(ct);
            foreach (var token in family)
            {
                token.Revoke(now);
            }

            await db.SaveChangesAsync(ct);
        }

        // Always 204: whether the token existed is not the caller's business.
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetMeAsync(
        ClaimsPrincipal principal,
        UserManager<SpaceCraftUser> userManager,
        CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(principal.Id().ToString());
        return user is null
            ? ApiResults.NotFound("The user")
            : TypedResults.Ok(new UserResponse(user.Id, user.Email ?? string.Empty, user.DisplayName));
    }

    private static async Task<TokenPair> IssueAndStoreAsync(
        SpaceCraftUser user,
        SpaceCraftDbContext db,
        ITokenService tokens,
        JwtOptions options,
        Guid? familyId,
        CancellationToken ct)
    {
        var pair = tokens.Issue(user);

        db.RefreshTokens.Add(new RefreshToken(
            user.Id,
            tokens.HashRefreshToken(pair.RefreshToken),
            DateTimeOffset.UtcNow.Add(options.RefreshTokenLifetime),
            familyId));

        await db.SaveChangesAsync(ct);
        return pair;
    }

    private static AuthResponse ToResponse(TokenPair pair, SpaceCraftUser user) =>
        new(pair.AccessToken,
            pair.RefreshToken,
            pair.AccessTokenExpiresAt,
            new UserResponse(user.Id, user.Email ?? string.Empty, user.DisplayName));

    private static string MapErrorField(string code) => code switch
    {
        var c when c.Contains("Password", StringComparison.OrdinalIgnoreCase) => "password",
        var c when c.Contains("Email", StringComparison.OrdinalIgnoreCase) => "email",
        var c when c.Contains("UserName", StringComparison.OrdinalIgnoreCase) => "email",
        _ => "request",
    };
}
