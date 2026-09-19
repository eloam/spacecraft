using FluentValidation.TestHelper;
using SpaceCraft.Api.Features.Members.Requests;
using SpaceCraft.Domain.Organizations;
using Xunit;

namespace SpaceCraft.UnitTests;

/// <summary>
/// The shape rules the Members request records carry, now that their handlers no longer
/// check it: each refusal names the property under the key the web client reads it by,
/// with the message the handler used to give, and a well-formed request goes through.
/// </summary>
public sealed class MembersRequestValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_blank_email_is_refused(string? email)
    {
        var result = new AddMemberRequest.Validator()
            .TestValidate(new AddMemberRequest(email!, OrganizationRole.Member));

        // MembersPage.tsx reads errors.Email, case-sensitively, and shows it under the field.
        // One message, not one per rule: the blank fails NotEmpty and stops there.
        result.ShouldHaveValidationErrorFor(request => request.Email)
            .WithErrorMessage("Enter a valid email address.")
            .Only();
    }

    // Only an '@' is asked for, as the handler asked before: the invitation mail is the real
    // test of the address.
    [Theory]
    [InlineData("ada")]
    [InlineData("ada.example.com")]
    public void An_email_without_an_at_sign_is_refused(string email)
    {
        var result = new AddMemberRequest.Validator()
            .TestValidate(new AddMemberRequest(email, OrganizationRole.Member));

        result.ShouldHaveValidationErrorFor(request => request.Email)
            .WithErrorMessage("Enter a valid email address.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void An_invitation_to_a_number_that_is_not_a_role_is_refused(int role)
    {
        var result = new AddMemberRequest.Validator()
            .TestValidate(new AddMemberRequest("ada@example.com", (OrganizationRole)role));

        result.ShouldHaveValidationErrorFor(request => request.Role)
            .WithErrorMessage($"'{role}' is not a role.");
    }

    [Fact]
    public void A_well_formed_invitation_passes()
    {
        // Surrounding whitespace and capitals are the handler's to normalise before the
        // address is stored; the validator must not refuse them first.
        var result = new AddMemberRequest.Validator()
            .TestValidate(new AddMemberRequest(" Ada@Example.com ", OrganizationRole.Admin));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void A_role_change_to_a_number_that_is_not_a_role_is_refused(int role)
    {
        var result = new ChangeRoleRequest.Validator()
            .TestValidate(new ChangeRoleRequest((OrganizationRole)role));

        result.ShouldHaveValidationErrorFor(request => request.Role)
            .WithErrorMessage($"'{role}' is not a role.");
    }

    [Fact]
    public void A_role_change_to_a_real_role_passes()
    {
        var result = new ChangeRoleRequest.Validator()
            .TestValidate(new ChangeRoleRequest(OrganizationRole.Owner));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_blank_token_is_refused(string? token)
    {
        var result = new AcceptInvitationRequest.Validator()
            .TestValidate(new AcceptInvitationRequest(token!));

        result.ShouldHaveValidationErrorFor(request => request.Token)
            .WithErrorMessage("An invitation token is required.");
    }

    [Fact]
    public void A_token_passes()
    {
        var result = new AcceptInvitationRequest.Validator()
            .TestValidate(new AcceptInvitationRequest("ZmFrZS10b2tlbg"));

        result.ShouldNotHaveAnyValidationErrors();
    }
}
