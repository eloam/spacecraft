using FluentValidation;
using SpaceCraft.Domain.Common;
// Inside the record, the Slug property hides the Domain type of the same name.
using DomainSlug = SpaceCraft.Domain.Common.Slug;

namespace SpaceCraft.Api.Features.Organizations.Requests;

public sealed record CreateOrganizationRequest(string Name, string? Slug)
{
    public sealed class Validator : AbstractValidator<CreateOrganizationRequest>
    {
        public Validator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            RuleFor(request => request.Name)
                .Must(ResourceName.IsValid)
                .WithMessage(ResourceName.Rule);

            // A blank slug is not a bad one: the handler derives it from the name.
            RuleFor(request => request.Slug)
                .Must(slug => DomainSlug.IsValid(slug!.Trim()))
                .WithMessage("A slug may contain lowercase letters, digits and single hyphens only.")
                .When(request => !string.IsNullOrWhiteSpace(request.Slug));
        }
    }
}
