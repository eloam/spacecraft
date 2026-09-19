namespace SpaceCraft.Api.Features.Members;

public static class MemberEndpoints
{
    public static RouteGroupBuilder MapMemberEndpoints(this RouteGroupBuilder api)
    {
        // The guard's 404 to a non-member, once for the group: every route below opens with
        // it. Its 403 is declared by the routes that ask for more than Viewer, since a read a
        // Viewer may make cannot answer it.
        var group = api.MapGroup("/organizations/{organizationId:guid}/members")
            .WithTags("Members")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListMembers.HandleAsync)
            .WithSummary("Lists the people in this organization.");

        group.MapPost("/", InviteMember.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Invites an address to this organization. Membership starts when the invitation is accepted.");

        group.MapGet("/invitations", ListInvitations.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Lists invitations that are still open.");

        // Outside the organization group on purpose: whoever holds the invitation knows a
        // token and nothing else — not which organization it is for, which is precisely what
        // the token is there to establish.
        //
        // A POST for a read, because the token travels in the body. In a path it would be
        // written to every access log and proxy trace between here and the browser, and the
        // token is the whole grant.
        api.MapPost("/invitations/preview", PreviewInvitation.HandleAsync)
            .WithTags("Members")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Describes the organization an invitation is for, without spending it.");

        api.MapPost("/invitations/decline", DeclineInvitation.HandleAsync)
            .WithTags("Members")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Turns an invitation down, which puts its link out.");

        api.MapPost("/invitations/accept", AcceptInvitation.HandleAsync)
            .WithTags("Members")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Joins the organization an invitation was sent for.");

        group.MapDelete("/invitations/{invitationId:guid}", RevokeInvitation.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Withdraws an invitation that has not been accepted.");

        group.MapPatch("/{userId:guid}", ChangeMemberRole.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Changes a member's role.");

        group.MapDelete("/{userId:guid}", RemoveMember.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Removes a member from this organization.");

        return api;
    }
}
