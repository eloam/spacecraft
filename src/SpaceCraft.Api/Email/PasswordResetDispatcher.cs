using SpaceCraft.Api.Auth;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace SpaceCraft.Api.Email;

/// <summary>
/// Does the actual work of a password reset request, off the request thread: finds the
/// account, issues a token, and sends the link. See <see cref="PasswordResetRequests"/> for
/// why none of this happens inline.
/// </summary>
public sealed class PasswordResetDispatcher(
    BackgroundWorkQueue<PasswordResetRequest> requests,
    IServiceScopeFactory scopeFactory,
    IEmailSender email,
    IOptions<PublicUrlsOptions> publicUrls,
    ILogger<PasswordResetDispatcher> logger) : BackgroundService
{
    /// <summary>
    /// How long an emailed reset link stays usable. Short, because the token is a bearer
    /// credential sitting in a mailbox: long enough to step away from the keyboard, not long
    /// enough for an old forwarded email to matter.
    /// </summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in requests.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await HandleAsync(request.EmailAddress, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // No address in the message: this log must not become a list of who asked.
                logger.LogError(ex, "Handling a password reset request failed");
            }
        }
    }

    private async Task HandleAsync(string address, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SpaceCraftUser>>();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var user = await userManager.FindByEmailAsync(address);
        if (user is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // Supersede anything still outstanding, so only the newest link works.
        var outstanding = await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null && t.InvalidatedAt == null)
            .ToListAsync(ct);

        foreach (var token in outstanding)
        {
            token.Invalidate(now);
        }

        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        db.PasswordResetTokens.Add(
            new PasswordResetToken(user.Id, tokens.HashToken(raw), now.Add(TokenLifetime)));

        await db.SaveChangesAsync(ct);

        // The link points at the front end, which collects the new password and posts it back.
        var urls = publicUrls.Value;
        var link = $"{urls.AppRoot()}/reset-password?token={Uri.EscapeDataString(raw)}";

        await email.SendAsync(Emails.PasswordReset(address, link, TokenLifetime, urls.ApiRoot()), ct);
    }
}
