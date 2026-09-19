using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using SpaceCraft.Api.Email;
using SpaceCraft.Domain.Organizations;

namespace SpaceCraft.Api.Features.Members;

/// <summary>
/// Sends the invitation email, off the request thread.
/// </summary>
/// <remarks>
/// The link this carries is the whole grant. It goes to a mailbox rather than to an account,
/// which is the point of the flow: nothing in this system proves an address belongs to the
/// account using it, so the proof has to be the ability to read the address's mail.
/// </remarks>
public sealed class InvitationDispatcher(
    BackgroundWorkQueue<InvitationRequest> requests,
    IServiceScopeFactory scopeFactory,
    IEmailSender email,
    IOptions<PublicUrlsOptions> publicUrls,
    ILogger<InvitationDispatcher> logger) : BackgroundService
{
    /// <summary>
    /// How long an invitation stays open. Longer than a password reset — the recipient may be
    /// away, and nothing is at stake until they accept — and short enough that a forwarded
    /// mailbox does not carry a standing offer of membership.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in requests.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await HandleAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The id, never the address: this log must not become a list of who was invited.
                logger.LogError(ex, "Sending invitation {InvitationId} failed", request.InvitationId);
            }
        }
    }

    private async Task HandleAsync(InvitationRequest request, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceCraftDbContext>();

        var invitation = await db.OrganizationInvitations
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.InvitationId, ct);

        if (invitation is null)
        {
            // Withdrawn between the request and this worker picking it up. Sending it now
            // would offer membership the organization has already taken back.
            return;
        }

        var organization = await db.Organizations
            .AsNoTracking()
            .Where(o => o.Id == invitation.OrganizationId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync(ct);

        var invitedBy = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == invitation.InvitedByUserId)
            .Select(u => u.DisplayName ?? u.Email)
            .FirstOrDefaultAsync(ct);

        var urls = publicUrls.Value;
        var link = $"{urls.AppRoot()}/invitations/accept?token={Uri.EscapeDataString(request.RawToken)}";

        await email.SendAsync(
            MemberEmails.OrganizationInvitation(
                invitation.Email,
                organization ?? "an organization",
                invitedBy ?? "Someone",
                invitation.Role.ToString(),
                link,
                Lifetime,
                urls.ApiRoot()),
            ct);
    }
}
