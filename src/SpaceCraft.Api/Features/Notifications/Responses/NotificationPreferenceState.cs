using SpaceCraft.Domain.Notifications;

namespace SpaceCraft.Api.Features.Notifications.Responses;

/// <summary>One switch on the settings page: this kind, on this channel.</summary>
public sealed record NotificationPreferenceState(string Kind, string Channel, bool Enabled)
{
    /// <summary>
    /// Every switch the settings page shows, with the defaults filled in where nothing has
    /// been said. The page never has to know what the defaults are.
    /// </summary>
    public static List<NotificationPreferenceState> Matrix(List<NotificationPreference> stated) =>
    [
        .. from kind in NotificationDefaults.Kinds
           from channel in NotificationDefaults.Channels
           select new NotificationPreferenceState(
               kind.ToString(),
               channel.ToString(),
               NotificationAddressing.IsEnabled(stated, kind, channel))
    ];
}
