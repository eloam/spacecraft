using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Identity;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features;

public sealed record MemberResponse(
    Guid UserId,
    string Email,
    string? DisplayName,
    OrganizationRole Role,
    bool IsYou,
    DateTimeOffset JoinedAt);

public sealed record AddMemberRequest(string Email, OrganizationRole Role);

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

        group.MapPost("/", AddAsync)
            .Produces<MemberResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Adds an existing SpaceCraft account to this organization.");

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

    private static async Task<IResult> AddAsync(
        Guid organizationId,
        [FromBody] AddMemberRequest request,
        ClaimsPrincipal principal,
        UserManager<SpaceCraftUser> userManager,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        var caller = principal.Id();

        // Admin, not Member: adding people changes who can reach the organization's
        // credentials and publish under its packages.
        if (await access.DenyUnlessAsync(organizationId, caller, OrganizationRole.Admin, ct) is { } denied)
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

        var invitee = await userManager.FindByEmailAsync((request.Email ?? string.Empty).Trim());

        if (invitee is null)
        {
            // Says plainly that the address has no account. This is not the account oracle
            // the auth endpoints guard against: the caller is already an admin here, and
            // "invite failed for a reason we won't name" is unusable.
            return ApiResults.Invalid(
                nameof(request.Email),
                "No SpaceCraft account uses that address. They need to sign up first.");
        }

        try
        {
            organization.AddMember(invitee.Id, request.Role);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResults.Conflict(ex.Message);
        }

        await db.SaveChangesAsync(ct);

        var members = await LoadAsync(db, organizationId, caller, ct);
        var added = members.First(m => m.UserId == invitee.Id);

        return TypedResults.Created(
            $"/api/v1/organizations/{organizationId}/members/{invitee.Id}", added);
    }

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

        try
        {
            organization.RemoveMember(userId);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResults.Conflict(ex.Message);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

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
