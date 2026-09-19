using FluentValidation.TestHelper;
using SpaceCraft.Api.Features.Auth.Requests;
using Shouldly;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The shape checks the Auth handlers used to open with, now carried by each request record's
/// Validator: that they refuse what the handlers refused, with the words the handlers used,
/// and that they treat whitespace the way IsNullOrWhiteSpace did.
/// </summary>
public sealed class AuthRequestValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Registering_with_a_blank_email_is_refused(string? email)
    {
        var result = new RegisterRequest.Validator().TestValidate(new RegisterRequest(email!, "correct horse battery", null));

        result.ShouldHaveValidationErrorFor(request => request.Email)
            .WithErrorMessage("An email address is required.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Registering_with_a_blank_password_is_refused(string? password)
    {
        var result = new RegisterRequest.Validator().TestValidate(new RegisterRequest("ada@example.com", password!, null));

        result.ShouldHaveValidationErrorFor(request => request.Password)
            .WithErrorMessage("A password is required.");
    }

    [Fact]
    public void Registering_with_nothing_reports_both_fields_at_once()
    {
        var result = new RegisterRequest.Validator().TestValidate(new RegisterRequest("", "", null));

        result.ShouldHaveValidationErrorFor(request => request.Email);
        result.ShouldHaveValidationErrorFor(request => request.Password);
    }

    [Fact]
    public void Registering_with_an_email_and_a_password_passes()
    {
        var result = new RegisterRequest.Validator().TestValidate(new RegisterRequest("ada@example.com", "correct horse battery", "Ada"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Refreshing_with_a_blank_token_is_refused(string? token)
    {
        var result = new RefreshRequest.Validator().TestValidate(new RefreshRequest(token!));

        result.ShouldHaveValidationErrorFor(request => request.RefreshToken)
            .WithErrorMessage("A refresh token is required.");
    }

    [Fact]
    public void Refreshing_with_a_token_passes()
    {
        var result = new RefreshRequest.Validator().TestValidate(new RefreshRequest("opaque-refresh-token"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Asking_for_a_reset_with_a_blank_address_is_refused_once(string? email)
    {
        var result = new ForgotPasswordRequest.Validator().TestValidate(new ForgotPasswordRequest(email!));

        // Blank fails both rules of the property; the caller must hear the message once.
        result.ShouldHaveValidationErrorFor(request => request.Email)
            .WithErrorMessage("Enter a valid email address.");
        result.Errors.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("ada@")]
    [InlineData("@example.com")]
    public void Asking_for_a_reset_with_a_malformed_address_is_refused(string email)
    {
        var result = new ForgotPasswordRequest.Validator().TestValidate(new ForgotPasswordRequest(email));

        result.ShouldHaveValidationErrorFor(request => request.Email)
            .WithErrorMessage("Enter a valid email address.");
    }

    [Fact]
    public void Asking_for_a_reset_with_a_well_formed_address_passes()
    {
        var result = new ForgotPasswordRequest.Validator().TestValidate(new ForgotPasswordRequest("ada@example.com"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Resetting_with_a_blank_token_is_refused(string? token)
    {
        var result = new ResetPasswordRequest.Validator().TestValidate(new ResetPasswordRequest(token!, "correct horse battery"));

        result.ShouldHaveValidationErrorFor(request => request.Token)
            .WithErrorMessage("The reset link is incomplete.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Resetting_with_a_blank_new_password_is_refused(string? newPassword)
    {
        var result = new ResetPasswordRequest.Validator().TestValidate(new ResetPasswordRequest("emailed-token", newPassword!));

        result.ShouldHaveValidationErrorFor(request => request.NewPassword)
            .WithErrorMessage("Choose a new password.");
    }

    [Fact]
    public void Resetting_with_a_token_and_a_new_password_passes()
    {
        var result = new ResetPasswordRequest.Validator().TestValidate(new ResetPasswordRequest("emailed-token", "correct horse battery"));

        result.ShouldNotHaveAnyValidationErrors();
    }
}
