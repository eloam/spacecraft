using FluentValidation.TestHelper;
using SpaceCraft.Api.Features.Waitlist.Requests;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The plausibility rule the waitlist signup carries — the same one the handler applied
/// before, moved rather than rewritten: one @ with something on both sides, a dot after it,
/// no spaces, and no longer than the column that stores it.
/// </summary>
public sealed class WaitlistRequestValidationTests
{
    private const string NotAnAddress = "Enter a valid email address.";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_email_is_refused_once(string? email)
    {
        var request = new WaitlistSignupRequest(email!, Source: null);

        new WaitlistSignupRequest.Validator().TestValidate(request)
            .ShouldHaveValidationErrorFor(r => r.Email)
            .WithErrorMessage(NotAnAddress)
            .Only();
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("@example.com")]
    [InlineData("nobody@")]
    [InlineData("nobody@@example.com")]
    [InlineData("nobody@example")]
    [InlineData("no body@example.com")]
    [InlineData(" @example.com")]
    public void An_email_that_is_not_an_address_is_refused(string email)
    {
        var request = new WaitlistSignupRequest(email, Source: null);

        new WaitlistSignupRequest.Validator().TestValidate(request)
            .ShouldHaveValidationErrorFor(r => r.Email)
            .WithErrorMessage(NotAnAddress);
    }

    // The column holds 320 characters; a longer address used to be refused here and must
    // not become a background insert failure instead.
    [Fact]
    public void An_address_longer_than_the_column_is_refused()
    {
        var request = new WaitlistSignupRequest(new string('a', 310) + "@example.com", Source: null);

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
