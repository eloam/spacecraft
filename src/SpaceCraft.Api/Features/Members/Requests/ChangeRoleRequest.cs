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

            // Enums bind from any integer, and the handler used to write whatever arrived. A
            // value outside the model is not a role: below Viewer it makes a member every
            // permission check refuses, above Owner one nobody could demote.
            RuleFor(request => request.Role)
                .IsInEnum()
                .WithMessage("'{PropertyValue}' is not a role.");
        }
    }
}
