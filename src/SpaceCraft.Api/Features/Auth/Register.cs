using SpaceCraft.Api.Common;
using SpaceCraft.Api.Email;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Api.Features.Auth;

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);

public static class Register
{
    public static async Task<IResult> HandleAsync(
        [FromBody] RegisterRequest request,
        UserManager<SpaceCraftUser> userManager,
        SpaceCraftDbContext db,
        ITokenFactory tokenFactory,
        IOptions<JwtOptions> jwtOptions,
        IEmailSender email,
        IOptions<PublicUrlsOptions> publicUrls,
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
        var urls = publicUrls.Value;
        await email.SendAsync(AuthEmails.Welcome(user.Email!, urls.AppRoot(), user.DisplayName, urls.ApiRoot()), ct);

        var pair = await Sessions.IssueAsync(user, db, tokenFactory, familyId: null, ct);
        return TypedResults.Ok(AuthResponse.From(pair, user));
    }

    private static string MapErrorField(string code) => code switch
    {
        var c when c.Contains("Password", StringComparison.OrdinalIgnoreCase) => "password",
        var c when c.Contains("Email", StringComparison.OrdinalIgnoreCase) => "email",
        var c when c.Contains("UserName", StringComparison.OrdinalIgnoreCase) => "email",
        _ => "request",
    };
}
