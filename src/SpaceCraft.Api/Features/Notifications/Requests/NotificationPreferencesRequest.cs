using FluentValidation;
using SpaceCraft.Api.Features.Notifications.Responses;
using SpaceCraft.Domain.Notifications;

namespace SpaceCraft.Api.Features.Notifications.Requests;

public sealed record NotificationPreferencesRequest(List<NotificationPreferenceState> Preferences)
{
    public sealed class Validator : AbstractValidator<NotificationPreferencesRequest>
    {
        public Validator()
        {
            // Judged switch by switch, so every wrong name is reported under its own index
            // rather than the first one stopping the rest — which is why the cascade setting
            // sits on the child rules and not on the collection rule, where it would end the
            // walk at the first bad row. A null list has nothing to judge and passes, as it
            // always has.
            RuleForEach(request => request.Preferences).ChildRules(state =>
            {
                state.RuleLevelCascadeMode = CascadeMode.Stop;

                state.RuleFor(preference => preference.Kind)
                    .Must(BeAKindWeSend)
                    .WithMessage("'{PropertyValue}' is not a notification we send.");

                state.RuleFor(preference => preference.Channel)
                    .Must(BeAChannelWeDeliverOn)
                    .WithMessage("'{PropertyValue}' is not a channel we deliver on.");
            });
        }

        // Case-insensitive, like the handler that reads them: the page sends the names it was
        // given, and a hand-written request should not fail on capitalisation.
        private static bool BeAKindWeSend(string? value) =>
            Enum.TryParse<NotificationKind>(value, ignoreCase: true, out var kind)
            && NotificationDefaults.Kinds.Contains(kind);

        private static bool BeAChannelWeDeliverOn(string? value) =>
            Enum.TryParse<NotificationChannel>(value, ignoreCase: true, out var channel)
            && NotificationDefaults.Channels.Contains(channel);
    }
}
