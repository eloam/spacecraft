using FluentValidation.TestHelper;
using SpaceCraft.Api.Features.Account.Requests;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The shape rule the profile request carries: that it refuses what its handler refused
/// before, under the same key and with the same words, measured on the name as it will be
/// stored, and that what the handler still clears gets through to it.
/// </summary>
public sealed class AccountRequestValidationTests
{
    private const string TooLong = "Use 100 characters or fewer.";

    [Fact]
    public void A_display_name_over_100_characters_is_refused()
    {
        var request = new UpdateProfileRequest(new string('a', 101));

        new UpdateProfileRequest.Validator().TestValidate(request)
            .ShouldHaveValidationErrorFor(r => r.DisplayName)
            .WithErrorMessage(TooLong);
    }

    // The handler trims before storing, so padding is not part of the length.
    [Fact]
    public void Padding_does_not_count_towards_the_limit()
    {
        var request = new UpdateProfileRequest("  " + new string('a', 100) + "  ");

        new UpdateProfileRequest.Validator().TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ada Lovelace")]
    public void A_display_name_within_the_limit_or_none_at_all_is_accepted(string? displayName)
    {
        var request = new UpdateProfileRequest(displayName);

        new UpdateProfileRequest.Validator().TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }
}
