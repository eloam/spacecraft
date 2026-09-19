using SpaceCraft.Api.Features.Notifications.Responses;

namespace SpaceCraft.Api.Features.Notifications.Requests;

public sealed record NotificationPreferencesRequest(List<NotificationPreferenceState> Preferences);
