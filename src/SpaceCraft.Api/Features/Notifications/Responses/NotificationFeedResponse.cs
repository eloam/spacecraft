namespace SpaceCraft.Api.Features.Notifications.Responses;

public sealed record NotificationFeedResponse(int UnreadCount, List<NotificationResponse> Items);
