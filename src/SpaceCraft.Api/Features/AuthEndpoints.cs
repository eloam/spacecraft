using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using SpaceCraft.Api.Auth;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Common;
using SpaceCraft.Api.Email;
using SpaceCraft.Api.Extensions;
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

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

/// <summary>
/// Deliberately says nothing about whether the address is registered. The message is the
/// same either way, because a difference here is a way to test who has an account.
/// </summary>
public sealed record AcknowledgementResponse(string Message);

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
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Creates an account and returns a token pair.");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Exchanges credentials for a token pair.");

        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithSummary("Rotates a refresh token for a new token pair.");

        group.MapPost("/forgot-password", ForgotPasswordAsync)
            .AllowAnonymous()
            .Produces<AcknowledgementResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Emails a password reset link, if the address has an account.");

        group.MapPost("/reset-password", ResetPasswordAsync)
            .AllowAnonymous()
            .Produces<AcknowledgementResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesValidationProblem()
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .WithSummary("Sets a new password using an emailed reset token.");

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
        IEmailSender email,
        IConfiguration configuration,
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

        // Queued, not awaited: the account exists and the caller has their tokens, so a slow
        // or failing mail provider must not turn a successful registration into an error.
        var appUrl = (configuration["PublicUrls:App"] ?? "https://app.spacecraft.dev").TrimEnd('/');
        var apiUrl = (configuration["PublicUrls:Api"] ?? "https://api.spacecraft.dev").TrimEnd('/');
        await email.SendAsync(Emails.Welcome(user.Email!, appUrl, user.DisplayName, apiUrl), ct);

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

        // Checked before the password, not after. Asked the other way round, a locked account
        // still had every guess evaluated, so the lockout delayed nothing it was meant to.
        if (user is not null && await userManager.IsLockedOutAsync(user))
        {
            return ApiResults.Problem(
                "Account locked",
                "Too many failed attempts. Try again later.",
                StatusCodes.Status423Locked);
        }

        // Same response whether the account is missing or the password is wrong, so the
        // endpoint cannot be used to enumerate registered addresses.
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password ?? string.Empty))
        {
            // Counted here because nothing else does. CheckPasswordAsync does not touch
            // AccessFailedCount — only SignInManager.CheckPasswordSignInAsync does, and this
            // application registers Identity's core without one. So the ten-attempt lockout
            // configured in DependencyInjection was never armed, LockoutEnd was never set,
            // and the branch above could not be reached: a comment two files away claimed
            // "Identity's per-account lockout stops brute force against one account" while
            // nothing counted a single failure.
            if (user is not null)
            {
                await userManager.AccessFailedAsync(user);
            }

            return ApiResults.Problem(
                "Invalid credentials",
                "The email address or password is incorrect.",
                StatusCodes.Status401Unauthorized);
        }

        // A password that works clears the tally, or ten failures spread over months would
        // lock an account whose owner never mistyped anything twice in a row.
        if (await userManager.GetAccessFailedCountAsync(user) > 0)
        {
            await userManager.ResetAccessFailedCountAsync(user);
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

        var hash = tokens.HashToken(request.RefreshToken);
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

    private static IResult ForgotPasswordAsync(
        [FromBody] ForgotPasswordRequest request,
        BackgroundWorkQueue<PasswordResetRequest> resetRequests)
    {
        var address = (request.Email ?? string.Empty).Trim();

        if (address.Length == 0 || !address.Contains('@', StringComparison.Ordinal))
        {
            return ApiResults.Invalid(nameof(request.Email), "Enter a valid email address.");
        }

        // Everything past this point — looking the account up, issuing a token, sending the
        // mail — happens on a background worker. The handler must not branch on whether the
        // address is registered, because the time it takes to do so is itself an answer.
        resetRequests.Enqueue(new PasswordResetRequest(address));

        return TypedResults.Accepted(
            (string?)null,
            new AcknowledgementResponse(
                "If that address has an account, a reset link is on its way."));
    }

    private static async Task<IResult> ResetPasswordAsync(
        [FromBody] ResetPasswordRequest request,
        UserManager<SpaceCraftUser> userManager,
        SpaceCraftDbContext db,
        ITokenService tokens,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return ApiResults.Invalid(nameof(request.Token), "The reset link is incomplete.");
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return ApiResults.Invalid(nameof(request.NewPassword), "Choose a new password.");
        }

        // One message for every way a token can be unusable — unknown, expired, already
        // spent, superseded. Distinguishing them tells an attacker which guesses were close.
        var rejected = ApiResults.Problem(
            "Invalid reset link",
            "This reset link is no longer valid. Request a new one.",
            StatusCodes.Status400BadRequest);

        var now = DateTimeOffset.UtcNow;
        var hash = tokens.HashToken(request.Token);

        var stored = await db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null || !stored.IsRedeemable(now))
        {
            return rejected;
        }

        var user = await userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null)
        {
            return rejected;
        }

        // Identity owns the password itself: going through it applies the configured password
        // policy and rolls the security stamp. Our token governs authorisation, not the change.
        var identityToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, identityToken, request.NewPassword);

        if (!result.Succeeded)
        {
            // A policy failure is the caller's own input, so it is safe — and necessary — to
            // say what was wrong. The token stays unspent so they can try again.
            return ApiResults.Invalid(
                nameof(request.NewPassword),
                string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        stored.MarkUsed(now);

        // Whoever knew the old password may still hold a refresh token. Resetting is exactly
        // the moment to end every existing session rather than leave one open.
        var sessions = await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var session in sessions)
        {
            session.Revoke(now);
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(
            new AcknowledgementResponse("Your password has been changed. Sign in with it now."));
    }

    /// <summary>256 bits from the CSPRNG, URL-safe so it survives being pasted from an email.</summary>
    private static string GenerateResetToken() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    private static async Task<IResult> LogoutAsync(
        [FromBody] RefreshRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        ITokenService tokens,
        CancellationToken ct)
    {
        var userId = principal.Id();
        var hash = tokens.HashToken(request.RefreshToken ?? string.Empty);

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
        // A login starts a family; a refresh continues one. Either way the access token
        // carries the family id, so the session it belongs to is identifiable later.
        var session = familyId ?? Ids.New();
        var pair = tokens.Issue(user, session);

        db.RefreshTokens.Add(new RefreshToken(
            user.Id,
            tokens.HashToken(pair.RefreshToken),
            DateTimeOffset.UtcNow.Add(options.RefreshTokenLifetime),
            session));

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
