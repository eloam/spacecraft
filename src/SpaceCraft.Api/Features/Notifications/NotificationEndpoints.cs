namespace SpaceCraft.Api.Features.Notifications;

public static class NotificationEndpoints
{
    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var group = api.MapGroup("/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet("/", ListNotifications.HandleAsync)
            .WithSummary("The caller's notifications, newest first.");

        group.MapPost("/read-all", MarkAllRead.HandleAsync)
            .WithSummary("Marks every unread notification as read.");

        group.MapPost("/{notificationId:guid}/read", MarkRead.HandleAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Marks one notification as read.");

        group.MapDelete("/{notificationId:guid}", DeleteNotification.HandleAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Removes one notification from the caller's list.");

        group.MapDelete("/", ClearNotifications.HandleAsync)
            .WithSummary("Removes the caller's read notifications, and the unread ones on request.");

        group.MapGet("/preferences", GetPreferences.HandleAsync)
            .WithSummary("What the caller has chosen to receive, and on which channels.");

        group.MapPut("/preferences", SavePreferences.HandleAsync)
            .WithSummary("Replaces the caller's notification settings.");

        return api;
    }
}
