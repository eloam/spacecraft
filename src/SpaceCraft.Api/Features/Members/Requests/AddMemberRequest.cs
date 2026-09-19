using FluentValidation;
using SpaceCraft.Domain.Organizations;

namespace SpaceCraft.Api.Features.Members.Requests;

public sealed record AddMemberRequest(string Email, OrganizationRole Role)
{
    public sealed class Validator : AbstractValidator<AddMemberRequest>
    {
        public Validator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            // Judged trimmed, as the handler stores it, and asked only for an '@': the
            // invitation mail is the real test of the address.
            RuleFor(request => request.Email)
                .NotEmpty()
                .WithMessage("Enter a valid email address.")
                .Must(email => email.Trim().Contains('@', StringComparison.Ordinal))
                .WithMessage("Enter a valid email address.");

            // Enums bind from any integer. A value outside the model is not a role: below
            // Viewer it would be a member every permission check refuses, above Owner one
            // nobody could demote.
            RuleFor(request => request.Role)
                .IsInEnum()
                .WithMessage("'{PropertyValue}' is not a role.");
        }
    }
}
