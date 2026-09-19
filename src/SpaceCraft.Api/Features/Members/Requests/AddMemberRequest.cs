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

            RuleFor(request => request.Email)
                .NotEmpty()
                .WithMessage("Enter a valid email address.")
                .EmailAddress()
                .WithMessage("Enter a valid email address.");

            // An undefined value compares above every real role, so it would create a member
            // nobody could demote.
            RuleFor(request => request.Role)
                .IsInEnum()
                .WithMessage("'{PropertyValue}' is not a role.");
        }
    }
}
