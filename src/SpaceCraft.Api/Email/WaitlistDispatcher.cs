using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Waitlist;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Email;

/// <summary>
/// Records a waitlist signup and sends its confirmation link, off the request thread.
/// <para>
/// Same reasoning as <see cref="PasswordResetDispatcher"/>: the endpoint used to return
/// immediately for an already-confirmed address while writing to the database for a new one,
/// which was measurable — 3.5 ms against 7.5 ms — and told a caller whether an address had
/// already signed up, despite the identical response the code was careful to return.
/// </para>
/// </summary>
public sealed class WaitlistDispatcher(
    BackgroundWorkQueue<WaitlistSignup> signups,
    IServiceScopeFactory scopeFactory,
    IEmailSender email,
    IConfiguration configuration,
    ILogger<WaitlistDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var signup in signups.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await HandleAsync(signup, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // No address in the message: this log must not become the signup list.
                logger.LogError(ex, "Handling a waitlist signup failed");
            }
        }
    }

    private async Task HandleAsync(WaitlistSignup signup, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var address = signup.EmailAddress;
        var token = WaitlistTokens.Generate();

        var existing = await db.WaitlistEntries.FirstOrDefaultAsync(w => w.Email == address, ct);

        if (existing is not null)
        {
            if (existing.IsConfirmed)
            {
                // Nothing to do, and nothing was ever revealed: the caller has already been
                // given the same acknowledgement as everyone else.
                return;
            }

            existing.ReissueToken(WaitlistTokens.Hash(token));
        }
        else
        {
            db.WaitlistEntries.Add(new WaitlistEntry(
                address,
                WaitlistTokens.Hash(token),
                signup.Source,
                signup.HashedIp));
        }

        await db.SaveChangesAsync(ct);

        var baseUrl = configuration["PublicUrls:Api"] ?? "https://spacecraft.dev";

        var confirmUrl = $"{baseUrl}/api/v1/waitlist/confirm?token={token}";

        await email.SendAsync(Emails.WaitlistConfirmation(address, confirmUrl, baseUrl), ct);
    }
}
