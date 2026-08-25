using System.Security.Cryptography;
using System.Text;
using SpaceCraft.Api.Common;
using SpaceCraft.Api.Extensions;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features;

public sealed record WaitlistSignupRequest(string Email, string? Source);

public sealed record WaitlistSignupResponse(string Message);

/// <summary>
/// Backs the landing page's "Notify me" form while the product is pre-launch.
/// </summary>
public static class WaitlistEndpoints
{
    public static RouteGroupBuilder MapWaitlistEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/waitlist").WithTags("Waitlist").AllowAnonymous();

        group.MapPost("/", SignUpAsync)
            .RequireRateLimiting(ApiServiceExtensions.StrictLimiter)
            .Produces<WaitlistSignupResponse>()
            .ProducesValidationProblem()
            .WithSummary("Registers an email for launch notifications.");

        group.MapGet("/confirm", ConfirmAsync)
            .Produces<WaitlistSignupResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Confirms a waitlist signup from the emailed link.");

        return api;
    }

    private static async Task<IResult> SignUpAsync(
        [FromBody] WaitlistSignupRequest request,
        HttpContext http,
        SpaceCraftDbContext db,
        IEmailSender email,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var address = request.Email?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(address) || !IsPlausibleEmail(address))
        {
            return ApiResults.Invalid(nameof(request.Email), "Enter a valid email address.");
        }

        // Always the same response, whether this address is new, already pending, or already
        // confirmed. Otherwise the endpoint becomes a way to test who has signed up.
        const string Acknowledgement = "Check your inbox — we've sent you a confirmation link.";

        var token = GenerateToken();
        var existing = await db.WaitlistEntries.FirstOrDefaultAsync(w => w.Email == address, ct);

        if (existing is not null)
        {
            if (existing.IsConfirmed)
            {
                return TypedResults.Ok(new WaitlistSignupResponse(Acknowledgement));
            }

            existing.ReissueToken(Hash(token));
        }
        else
        {
            db.WaitlistEntries.Add(new WaitlistEntry(
                address,
                Hash(token),
                request.Source,
                HashIp(http.Connection.RemoteIpAddress?.ToString())));
        }

        await db.SaveChangesAsync(ct);

        var baseUrl = configuration["PublicUrls:Api"] ?? "https://spacecraft.dev";
        await email.SendAsync(new EmailMessage(
            address,
            "Confirm your SpaceCraft waitlist signup",
            $"""
            Thanks for your interest in SpaceCraft.

            Confirm your email to join the waitlist:
            {baseUrl}/api/v1/waitlist/confirm?token={token}

            If you didn't request this, ignore this message — no account is created.
            """), ct);

        return TypedResults.Ok(new WaitlistSignupResponse(Acknowledgement));
    }

    private static async Task<IResult> ConfirmAsync(
        [FromQuery] string token,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return ApiResults.Invalid(nameof(token), "A confirmation token is required.");
        }

        var hash = Hash(token);
        var entry = await db.WaitlistEntries.FirstOrDefaultAsync(w => w.ConfirmationTokenHash == hash, ct);

        if (entry is null)
        {
            return ApiResults.Problem(
                "Invalid link",
                "This confirmation link is not valid. Request a new one from the signup form.",
                StatusCodes.Status400BadRequest);
        }

        entry.Confirm(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new WaitlistSignupResponse("You're on the list. We'll be in touch at launch."));
    }

    /// <summary>
    /// Deliberately permissive. Full RFC 5322 validation rejects addresses that work, and
    /// the confirmation email is the real check.
    /// </summary>
    private static bool IsPlausibleEmail(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at > 0
            && at < value.Length - 1
            && value.Length <= 320
            && value.IndexOf('@', at + 1) < 0
            && value.LastIndexOf('.') > at
            && !value.Contains(' ', StringComparison.Ordinal);
    }

    private static string GenerateToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>
    /// Stores a hash rather than the address itself: enough to rate-limit repeat abuse,
    /// without retaining a personal identifier for everyone who visits the landing page.
    /// </summary>
    private static string? HashIp(string? ip) =>
        string.IsNullOrWhiteSpace(ip) ? null : Hash(ip);
}
