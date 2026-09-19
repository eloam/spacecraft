using FluentValidation;
using SpaceCraft.Domain.Organizations;

namespace SpaceCraft.Api.Features.Members.Requests;

public sealed record ChangeRoleRequest(OrganizationRole Role)
{
    public sealed class Validator : AbstractValidator<ChangeRoleRequest>
    {
        public Validator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            // An undefined value compares above every real role, so once written it would
            // make a member nobody could demote.
            RuleFor(request => request.Role)
                .IsInEnum()
                .WithMessage("'{PropertyValue}' is not a role.");
        }
    }
}
