using FluentValidation;
using SpaceCraft.Domain.Common;

namespace SpaceCraft.Api.Features.Organizations.Requests;

public sealed record RenameRequest(string Name)
{
    public sealed class Validator : AbstractValidator<RenameRequest>
    {
        public Validator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            RuleFor(request => request.Name)
                .Must(ResourceName.IsValid)
                .WithMessage(ResourceName.Rule);
        }
    }
}
