using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Notifications;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Notifications;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using SpaceCraft.Api.Email;
using SpaceCraft.Api.Auth;
namespace SpaceCraft.Api.Features;

public sealed record MemberResponse(
    Guid UserId,
    string Email,
    string? DisplayName,
    OrganizationRole Role,
    bool IsYou,
    DateTimeOffset JoinedAt);

public sealed record AddMemberRequest(string Email, OrganizationRole Role);

/// <summary>What an outstanding invitation looks like to the admin who sent it.</summary>
public sealed record InvitationResponse(
    Guid Id,
    string Email,
    OrganizationRole Role,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);

public sealed record AcceptInvitationRequest(string Token);

/// <summary>
/// What somebody holding an invitation is shown before deciding.
/// </summary>
/// <remarks>
/// Everything here is already implied by the invitation itself: the email names the
/// organization and who sent it, and accepting would show the rest a moment later. What the
/// projects add is the answer to "is this the right Acme?", which is the question somebody
/// looking at a link from a stranger actually has.
/// </remarks>
public sealed record InvitationPreviewResponse(
    string OrganizationName,
    string OrganizationSlug,
    OrganizationRole Role,
    /// <summary>The name the inviter goes by, or the address they signed up with.</summary>
    string InvitedBy,
    DateTimeOffset ExpiresAt,
    int MemberCount,
    List<string> Projects,
    /// <summary>Whether the signed-in account is already in this organization.</summary>
    bool AlreadyMember);

public sealed record ChangeRoleRequest(OrganizationRole Role);

public static class MemberEndpoints
{
    public static RouteGroupBuilder MapMemberEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations/{organizationId:guid}/members")
            .WithTags("Members")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/", ListAsync)
            .Produces<List<MemberResponse>>()
            .WithSummary("Lists the people in this organization.");

        group.MapPost("/", InviteAsync)
            .Produces<InvitationResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Invites an address to this organization. Membership starts when the invitation is accepted.");

        group.MapGet("/invitations", ListInvitationsAsync)
            .Produces<List<InvitationResponse>>()
            .WithSummary("Lists invitations that are still open.");

        // Outside the organization group on purpose: whoever holds the invitation knows a
        // token and nothing else — not which organization it is for, which is precisely what
        // the token is there to establish.
        //
        // A POST for a read, because the token travels in the body. In a path it would be
        // written to every access log and proxy trace between here and the browser, and the
        // token is the whole grant.
        api.MapPost("/invitations/preview", PreviewAsync)
            .WithTags("Members")
            .RequireAuthorization()
            .Produces<InvitationPreviewResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Describes the organization an invitation is for, without spending it.");

        api.MapPost("/invitations/decline", DeclineAsync)
            .WithTags("Members")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Turns an invitation down, which puts its link out.");

        api.MapPost("/invitations/accept", AcceptAsync)
            .WithTags("Members")
            .RequireAuthorization()
            .Produces<MemberResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Joins the organization an invitation was sent for.");

        group.MapDelete("/invitations/{invitationId:guid}", RevokeInvitationAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Withdraws an invitation that has not been accepted.");

        group.MapPatch("/{userId:guid}", ChangeRoleAsync)
            .Produces<MemberResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Changes a member's role.");

        group.MapDelete("/{userId:guid}", RemoveAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Removes a member from this organization.");

        return api;
    }

    private static async Task<IResult> ListAsync(
        Guid organizationId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        var caller = principal.Id();

        if (await access.DenyUnlessAsync(organizationId, caller, OrganizationRole.Viewer, ct) is { } denied)
        {
            return denied;
        }

        var members = await LoadAsync(db, organizationId, caller, ct);
        return TypedResults.Ok(members);
    }

    /// <summary>
    /// Sends an invitation to an address. Nobody joins until it is accepted.
    /// </summary>
    /// <remarks>
    /// This used to resolve the address to whatever account already used it and add that
    /// account on the spot. Nothing in this system proves an address belongs to the account
    /// holding it — <c>EmailConfirmed</c> is written by a migration and read by nobody — so
    /// registering a colleague's work address was enough to be handed their organization the
    /// day an admin invited them. The proof now lives where it belongs: a token goes to the
    /// mailbox, and only someone who can read that mailbox can accept.
    /// <para>
    /// The invitation is also written for an address with no account at all, which the old
    /// endpoint refused. Being invited is a reason to sign up, and refusing was an account
    /// oracle offered to every admin.
    /// </para>
    /// </remarks>
    private static async Task<IResult> InviteAsync(
        Guid organizationId,
        [FromBody] AddMemberRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        BackgroundWorkQueue<InvitationRequest> invitations,
        CancellationToken ct)
    {
        var caller = principal.Id();

        // Admin, not Member: adding people changes who can reach the organization's
        // credentials and publish under its packages.
        if (await access.DenyUnlessAsync(organizationId, caller, OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        var address = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (address.Length == 0 || !address.Contains('@', StringComparison.Ordinal))
        {
            return ApiResults.Invalid(nameof(request.Email), "Enter a valid email address.");
        }

        if (!Enum.IsDefined(request.Role))
        {
            // An undefined value compares above every real role, so it would create a member
            // nobody could demote.
            return ApiResults.Invalid(nameof(request.Role), $"'{request.Role}' is not a role.");
        }

        // Nobody grants a role they do not hold — the same cap the role-change endpoint
        // applies, checked here because the domain knows roles but not who is asking.
        var callerRole = await access.GetRoleAsync(organizationId, caller, ct);

        if (callerRole is not { } held || request.Role > held)
        {
            return ApiResults.Problem(
                "Forbidden",
                $"Granting the {request.Role} role takes that role or higher.",
                StatusCodes.Status403Forbidden);
        }

        var now = DateTimeOffset.UtcNow;

        // Superseded rather than stacked: an older link in an old email stops working the
        // moment a newer invitation is sent, the same rule password resets follow.
        var outstanding = await db.OrganizationInvitations
            .Where(i => i.OrganizationId == organizationId
                        && i.Email == address
                        && i.AcceptedAt == null
                        && i.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var previous in outstanding)
        {
            previous.Revoke(now);
        }

        var raw = MailedTokens.Generate();
        var invitation = new OrganizationInvitation(
            organizationId,
            address,
            request.Role,
            caller,
            MailedTokens.Hash(raw),
            now.Add(InvitationDispatcher.Lifetime));

        db.OrganizationInvitations.Add(invitation);
        await db.SaveChangesAsync(ct);

        // Written first, sent after: the admin must see the invitation in the listing whether
        // or not a mail relay is having a bad afternoon, and a relay outage must not lose it.
        invitations.Enqueue(new InvitationRequest(invitation.Id, raw));

        return TypedResults.Accepted(
            $"/api/v1/organizations/{organizationId}/members/invitations/{invitation.Id}",
            ToResponse(invitation));
    }

    /// <summary>
    /// Says what the invitation is for, and spends nothing.
    /// </summary>
    /// <remarks>
    /// The page this feeds used to accept on arrival: opening the link joined the
    /// organization, and the first thing anybody learned about it was that they were in it.
    /// An invitation is an offer, and an offer that cannot be read before it is taken is not
    /// much of one.
    /// <para>
    /// The refusal is the same single answer accepting gives — a token that never existed, one
    /// already spent, one withdrawn and one that lapsed are not told apart, because doing so
    /// would describe an organization to somebody with no standing in it.
    /// </para>
    /// </remarks>
    private static async Task<IResult> PreviewAsync(
        [FromBody] AcceptInvitationRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return ApiResults.Invalid(nameof(request.Token), "An invitation token is required.");
        }

        var now = DateTimeOffset.UtcNow;
        var invitation = await db.OrganizationInvitations
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.TokenHash == MailedTokens.Hash(request.Token), ct);

        if (invitation is null || !invitation.IsRedeemable(now))
        {
            return ApiResults.NotFound("A usable invitation");
        }

        var organization = await db.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == invitation.OrganizationId, ct);

        if (organization is null)
        {
            return ApiResults.NotFound("A usable invitation");
        }

        // Names only, and few. Enough to recognise which Acme this is, which is the question
        // somebody looking at a link from a half-remembered colleague actually has.
        var projects = await db.ApiProjects
            .AsNoTracking()
            .Where(p => p.OrganizationId == organization.Id)
            .OrderByDescending(p => p.CreatedAt)
            .Take(5)
            .Select(p => p.Name)
            .ToListAsync(ct);

        var memberCount = await db.OrganizationMembers
            .CountAsync(m => m.OrganizationId == organization.Id, ct);

        var invitedBy = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == invitation.InvitedByUserId)
            .Select(u => u.DisplayName ?? u.Email)
            .FirstOrDefaultAsync(ct);

        var userId = principal.Id();
        var alreadyMember = await db.OrganizationMembers
            .AnyAsync(m => m.OrganizationId == organization.Id && m.UserId == userId, ct);

        return TypedResults.Ok(new InvitationPreviewResponse(
            organization.Name,
            organization.Slug,
            invitation.Role,
            invitedBy ?? "Someone",
            invitation.ExpiresAt,
            memberCount,
            projects,
            alreadyMember));
    }

    /// <summary>
    /// Turns the invitation down.
    /// </summary>
    /// <remarks>
    /// Recorded as a decline rather than as a revocation: the admin withdrawing an invitation
    /// and the recipient refusing it are different events, and only the second is an answer.
    /// Either way the link stops working, which is what the person clicking this wants.
    /// </remarks>
    private static async Task<IResult> DeclineAsync(
        [FromBody] AcceptInvitationRequest request,
        SpaceCraftDbContext db,
        INotificationPublisher notifications,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return ApiResults.Invalid(nameof(request.Token), "An invitation token is required.");
        }

        var now = DateTimeOffset.UtcNow;
        var invitation = await db.OrganizationInvitations
            .FirstOrDefaultAsync(i => i.TokenHash == MailedTokens.Hash(request.Token), ct);

        if (invitation is null || !invitation.IsRedeemable(now))
        {
            return ApiResults.NotFound("A usable invitation");
        }

        invitation.Decline(now);
        await db.SaveChangesAsync(ct);

        var organization = await db.Organizations
            .AsNoTracking()
            .Where(o => o.Id == invitation.OrganizationId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync(ct);

        // To the people who answer for the organization rather than to the sender alone: an
        // invitation is the organization's, and whoever picks up the thread next needs to know
        // it was refused rather than still travelling. Nobody is excluded — the person who
        // declined is not a member and hears nothing.
        await notifications.PublishToGovernanceAsync(
            invitation.OrganizationId,
            actor: null,
            new NotificationContent(
                NotificationKind.InvitationDeclined,
                $"{invitation.Email} declined the invitation to {organization ?? "your organization"}",
                // Names them again rather than saying "they": the reader may be an admin who
                // did not send this one, and the answer to "who?" should not depend on having
                // read the line above.
                $"{invitation.Email} was invited as {invitation.Role} and turned it down.",
                "Nobody has been added to the organization. Send another invitation if they "
                    + "should still have access.",
                $"/orgs/{invitation.OrganizationId}/members",
                "View members"),
            ct);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Joins the organization the token was issued for.
    /// </summary>
    /// <remarks>
    /// The token is the whole proof, and it proves the one thing that was missing: whoever
    /// presents it could read the mailbox the invitation was sent to. Deliberately not checked
    /// against the signed-in account's own address — that address has never been verified
    /// either, so matching it would be checking one unproven claim against another. Forwarding
    /// an invitation to a colleague is also a thing people do, and the organization's admin can
    /// see who accepted.
    /// </remarks>
    private static async Task<IResult> AcceptAsync(
        [FromBody] AcceptInvitationRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        INotificationPublisher notifications,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return ApiResults.Invalid(nameof(request.Token), "An invitation token is required.");
        }

        var now = DateTimeOffset.UtcNow;
        var hash = MailedTokens.Hash(request.Token);

        // Looked up by hash, so the comparison is an index seek rather than a string compare
        // whose timing could be measured.
        var invitation = await db.OrganizationInvitations.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);

        // One answer for a token that never existed, one that was spent, one that was
        // withdrawn and one that lapsed. Telling them apart would say something about an
        // organization to somebody who has no standing in it.
        if (invitation is null || !invitation.IsRedeemable(now))
        {
            return ApiResults.NotFound("A usable invitation");
        }

        var organization = await db.Organizations
            .Include(o => o.Members)
            .FirstOrDefaultAsync(o => o.Id == invitation.OrganizationId, ct);

        if (organization is null)
        {
            // The organization went away while the invitation sat in a mailbox.
            invitation.Revoke(now);
            await db.SaveChangesAsync(ct);

            return ApiResults.NotFound("A usable invitation");
        }

        var userId = principal.Id();

        if (organization.Members.Any(m => m.UserId == userId))
        {
            // Spending the invitation anyway, so a second copy of the email cannot be held
            // in reserve against a later change of role.
            invitation.Accept(userId, now);
            await db.SaveChangesAsync(ct);

            return ApiResults.Conflict("You are already a member of this organization.");
        }

        try
        {
            organization.AddMember(userId, invitation.Role);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResults.Conflict(ex.Message);
        }

        invitation.Accept(userId, now);
        await db.SaveChangesAsync(ct);

        // After the save, so nothing is announced that did not happen. The joiner is the
        // actor and hears nothing: they are looking at the result of their own click.
        var joined = await DescribeAsync(db, userId, ct);

        await notifications.PublishToGovernanceAsync(
            organization.Id,
            actor: userId,
            new NotificationContent(
                NotificationKind.MemberJoined,
                $"{joined.Shortly} joined {organization.Name}",
                $"{joined} accepted an invitation and holds the {invitation.Role} role.",
                Detail: null,
                $"/orgs/{organization.Id}/members",
                "View members"),
            ct);

        var members = await LoadAsync(db, invitation.OrganizationId, userId, ct);
        return TypedResults.Ok(members.First(m => m.UserId == userId));
    }


    private static async Task<IResult> ListInvitationsAsync(
        Guid organizationId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        // Admin, like sending one: an outstanding invitation names an address, and who has
        // been asked to join is not general knowledge inside an organization.
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        var now = DateTimeOffset.UtcNow;

        // The shared predicate, not a copy of it. This list once carried its own and was not
        // told when declining was added, so an invitation somebody had already refused went on
        // being offered as pending — with a link that no longer worked.
        var open = await db.OrganizationInvitations
            .AsNoTracking()
            .Where(i => i.OrganizationId == organizationId)
            .Where(OrganizationInvitation.Usable(now))
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

        return TypedResults.Ok(open.Select(ToResponse).ToList());
    }

    private static async Task<IResult> RevokeInvitationAsync(
        Guid organizationId,
        Guid invitationId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        // Scoped to the organization that was authorised, not merely to the id: an invitation
        // id is a guid somebody could have been told.
        var invitation = await db.OrganizationInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.OrganizationId == organizationId, ct);

        if (invitation is null)
        {
            return ApiResults.NotFound("The invitation");
        }

        invitation.Revoke(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }

    private static InvitationResponse ToResponse(OrganizationInvitation invitation) =>
        new(invitation.Id, invitation.Email, invitation.Role, invitation.ExpiresAt, invitation.CreatedAt);

    private static async Task<IResult> ChangeRoleAsync(
        Guid organizationId,
        Guid userId,
        [FromBody] ChangeRoleRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        var caller = principal.Id();

        if (await access.DenyUnlessAsync(organizationId, caller, OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        var organization = await db.Organizations
            .Include(o => o.Members)
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        if (organization is null || organization.Members.All(m => m.UserId != userId))
        {
            return ApiResults.NotFound("The member");
        }

        // Nobody grants a role they do not hold, and nobody grants one to themselves.
        //
        // Admin was enough to reach here, and Admin could therefore write Owner onto its own
        // membership — then demote the real owner and delete the organization, the one action
        // deletion deliberately reserves because "an admin was added by someone else". The
        // same rule protects the other direction: an Admin must not demote an Owner, or the
        // reservation is worked around from below instead of above.
        var callerRole = await access.GetRoleAsync(organizationId, caller, ct);
        var currentRole = organization.Members.Single(m => m.UserId == userId).Role;

        if (userId == caller && request.Role != currentRole)
        {
            return ApiResults.Problem(
                "Forbidden",
                "You cannot change your own role. Ask another owner or admin.",
                StatusCodes.Status403Forbidden);
        }

        if (callerRole is not { } held || request.Role > held)
        {
            return ApiResults.Problem(
                "Forbidden",
                $"Granting the {request.Role} role takes that role or higher.",
                StatusCodes.Status403Forbidden);
        }

        if (currentRole > held)
        {
            return ApiResults.Problem(
                "Forbidden",
                $"Changing the role of an {currentRole} takes that role or higher.",
                StatusCodes.Status403Forbidden);
        }

        try
        {
            organization.ChangeMemberRole(userId, request.Role);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResults.Conflict(ex.Message);
        }

        await db.SaveChangesAsync(ct);

        var members = await LoadAsync(db, organizationId, caller, ct);
        return TypedResults.Ok(members.First(m => m.UserId == userId));
    }

    private static async Task<IResult> RemoveAsync(
        Guid organizationId,
        Guid userId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        INotificationPublisher notifications,
        CancellationToken ct)
    {
        var caller = principal.Id();

        // Anyone may remove themselves; removing someone else takes Admin.
        var required = caller == userId ? OrganizationRole.Viewer : OrganizationRole.Admin;

        if (await access.DenyUnlessAsync(organizationId, caller, required, ct) is { } denied)
        {
            return denied;
        }

        var organization = await db.Organizations
            .Include(o => o.Members)
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        if (organization is null)
        {
            return ApiResults.NotFoundOrForbidden();
        }

        // Nobody removes a member who outranks them. Without this an Admin evicted the Owner
        // and became the senior member of an organization they were merely invited into —
        // the same escalation as granting oneself Owner, reached by subtraction.
        if (caller != userId)
        {
            var callerRole = await access.GetRoleAsync(organizationId, caller, ct);
            var target = organization.Members.FirstOrDefault(m => m.UserId == userId);

            if (target is not null && (callerRole is not { } held || target.Role > held))
            {
                return ApiResults.Problem(
                    "Forbidden",
                    $"Removing an {target.Role} takes that role or higher.",
                    StatusCodes.Status403Forbidden);
            }
        }

        // Read before the removal takes it away: a notification that cannot say what someone
        // held says nothing an admin can act on.
        var heldRole = organization.Members.FirstOrDefault(m => m.UserId == userId)?.Role;

        try
        {
            organization.RemoveMember(userId);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResults.Conflict(ex.Message);
        }

        await db.SaveChangesAsync(ct);

        var left = await DescribeAsync(db, userId, ct);
        var themselves = caller == userId;

        // The person it happened to, first — and only when it happened to them rather than
        // by them. Somebody who left does not need to be told they left.
        if (!themselves)
        {
            await notifications.PublishToPersonAsync(
                organizationId,
                userId,
                new NotificationContent(
                    NotificationKind.RemovedFromOrganization,
                    $"You were removed from {organization.Name}",
                    $"{await DescribeAsync(db, caller, ct)} removed you from the organization. "
                        + "Its projects, runs and packages are no longer yours to reach.",
                    heldRole is { } held ? $"You held the {held} role." : null,
                    // Not the organization: it is the one page this notification is about and
                    // the one page its reader can no longer open.
                    "/",
                    "Go to your organizations"),
                ct);
        }

        await notifications.PublishToGovernanceAsync(
            organizationId,
            actor: caller,
            new NotificationContent(
                NotificationKind.MemberLeft,
                themselves
                    ? $"{left.Shortly} left {organization.Name}"
                    : $"{left.Shortly} was removed from {organization.Name}",
                themselves
                    ? $"{left} removed themselves from the organization."
                    : $"{await DescribeAsync(db, caller, ct)} removed {left} from the organization.",
                heldRole is { } role ? $"{left.Shortly} held the {role} role." : null,
                $"/orgs/{organizationId}/members",
                "View members"),
            ct);

        return TypedResults.NoContent();
    }

    /// <summary>Who somebody is: the name they gave, if any, and the address behind it.</summary>
    private sealed record Person(string? Name, string Email)
    {
        /// <summary>The name to put in a title, which is also an email's subject line.</summary>
        public string Shortly => string.IsNullOrWhiteSpace(Name) ? Email : Name;

        /// <summary>
        /// Both, for the body.
        /// </summary>
        /// <remarks>
        /// A display name alone answers "who" only for somebody who already knows them. The
        /// address is the account, and the account is what an admin reading this acts on —
        /// so every sentence that names a person in a notification carries it.
        /// </remarks>
        public override string ToString() =>
            string.IsNullOrWhiteSpace(Name) ? Email : $"{Name} ({Email})";
    }

    private static async Task<Person> DescribeAsync(
        SpaceCraftDbContext db, Guid userId, CancellationToken ct) =>
        await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new Person(u.DisplayName, u.Email ?? ""))
            .FirstOrDefaultAsync(ct) ?? new Person(null, "an account that no longer exists");

    /// <summary>
    /// Members joined to their accounts. Identity lives in its own tables, so the name and
    /// address are not on the membership row.
    /// </summary>
    private static async Task<List<MemberResponse>> LoadAsync(
        SpaceCraftDbContext db, Guid organizationId, Guid caller, CancellationToken ct)
    {
        var rows =
            from member in db.OrganizationMembers.AsNoTracking()
            where member.OrganizationId == organizationId
            join user in db.Users.AsNoTracking() on member.UserId equals user.Id
            select new { member, user };

        var loaded = await rows.ToListAsync(ct);

        return loaded
            .Select(row => new MemberResponse(
                row.member.UserId,
                row.user.Email ?? string.Empty,
                row.user.DisplayName,
                row.member.Role,
                row.member.UserId == caller,
                row.member.CreatedAt))
            .OrderByDescending(m => m.Role)
            .ThenBy(m => m.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
