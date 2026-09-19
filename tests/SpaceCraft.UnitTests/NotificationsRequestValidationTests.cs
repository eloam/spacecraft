using FluentValidation.TestHelper;
using SpaceCraft.Api.Features.Notifications.Requests;
using SpaceCraft.Api.Features.Notifications.Responses;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// What a notification settings request must look like before the handler sees it: every
/// switch names a kind we send and a channel we deliver on, spelled however the caller likes,
/// and a wrong one is reported under the index of the row it sits in.
/// </summary>
public sealed class NotificationsRequestValidationTests
{
    private static readonly NotificationPreferencesRequest.Validator Validator = new();

    [Fact]
    public void An_unknown_kind_is_refused_under_its_index()
    {
        var request = new NotificationPreferencesRequest([new NotificationPreferenceState("RunExploded", "Email", true)]);

        Validator.TestValidate(request)
            .ShouldHaveValidationErrorFor("Preferences[0].Kind")
            .WithErrorMessage("'RunExploded' is not a notification we send.");
    }

    [Fact]
    public void An_unknown_channel_is_refused_under_its_index()
    {
        var request = new NotificationPreferencesRequest(
        [
            new NotificationPreferenceState("RunCompleted", "Email", true),
            new NotificationPreferenceState("RunCompleted", "Pigeon", true),
        ]);

        Validator.TestValidate(request)
            .ShouldHaveValidationErrorFor("Preferences[1].Channel")
            .WithErrorMessage("'Pigeon' is not a channel we deliver on.");
    }

    [Fact]
    public void Names_are_accepted_whatever_their_case()
    {
        var request = new NotificationPreferencesRequest([new NotificationPreferenceState("runcompleted", "EMAIL", false)]);

        Validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void A_request_with_no_list_at_all_is_tolerated()
    {
        var request = new NotificationPreferencesRequest(null!);

        Validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Every_switch_the_settings_page_shows_is_accepted()
    {
        var request = new NotificationPreferencesRequest(NotificationPreferenceState.Matrix([]));

        Validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }
}
