using SpaceCraft.Api.Features.Auth;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpaceCraft.Api.Email;
using SpaceCraft.Domain.Identity;

namespace SpaceCraft.Api.Features.Auth;

/// <summary>
/// Does the actual work of a password reset request, off the request thread: finds the
/// account, issues a token, and sends the link. See <see cref="ForgotPassword"/> for why
/// none of this happens inline.
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

        var user = await userManager.FindByEmailAsync(address);
        if (user is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // Supersede anything still outstanding, so only the newest link works.
        await PasswordResets.InvalidateAsync(db, user.Id, now, ct);

        var raw = OpaqueTokens.Generate();
        db.PasswordResetTokens.Add(
            new PasswordResetToken(user.Id, OpaqueTokens.Hash(raw), now.Add(TokenLifetime)));

        await db.SaveChangesAsync(ct);

        // The link points at the front end, which collects the new password and posts it back.
        var urls = publicUrls.Value;
        var link = $"{urls.AppRoot()}/reset-password?token={Uri.EscapeDataString(raw)}";

        // To the address the account holds, not the one that was typed. Identity looks the
        // account up by normalised address, so "Alice@Example.com" finds the account Register
        // stored as alice@example.com — and the link is a bearer credential, so it goes to the
        // spelling on the account, not to whichever one the request happened to use.
        await email.SendAsync(AuthEmails.PasswordReset(user.Email!, link, TokenLifetime, urls.ApiRoot()), ct);
    }
}
