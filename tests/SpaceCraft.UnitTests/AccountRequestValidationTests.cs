using FluentValidation.TestHelper;
using SpaceCraft.Api.Features.Account.Requests;
using SpaceCraft.Api.Features.Waitlist.Requests;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The shape rules the Account and Waitlist request records carry: that each refuses what
/// its handler refused before, under the same key and with the same words, and that what the
/// handler still trims or clears gets through to it.
/// </summary>
public sealed class AccountRequestValidationTests
{
    private const string TooLong = "Use 100 characters or fewer.";
    private const string NotAnAddress = "Enter a valid email address.";

    [Fact]
    public void A_display_name_over_100_characters_is_refused()
    {
        var request = new UpdateProfileRequest(new string('a', 101));

        new UpdateProfileRequest.Validator().TestValidate(request)
            .ShouldHaveValidationErrorFor(r => r.DisplayName)
            .WithErrorMessage(TooLong);
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_email_is_refused(string? email)
    {
        var request = new WaitlistSignupRequest(email!, Source: null);

        new WaitlistSignupRequest.Validator().TestValidate(request)
            .ShouldHaveValidationErrorFor(r => r.Email)
            .WithErrorMessage(NotAnAddress);
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("@example.com")]
    [InlineData("nobody@")]
    [InlineData("nobody@@example.com")]
    public void An_email_that_is_not_an_address_is_refused(string email)
    {
        var request = new WaitlistSignupRequest(email, Source: null);

        new WaitlistSignupRequest.Validator().TestValidate(request)
            .ShouldHaveValidationErrorFor(r => r.Email)
            .WithErrorMessage(NotAnAddress);
    }

    // The padded, mixed-case address is what the handler trims and lower-cases; the validator
    // must not refuse it first.
    [Theory]
    [InlineData("nobody@example.com")]
    [InlineData("  Nobody@Example.com  ")]
    public void A_plausible_address_is_accepted(string email)
    {
        var request = new WaitlistSignupRequest(email, Source: "landing");

        new WaitlistSignupRequest.Validator().TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }
}
