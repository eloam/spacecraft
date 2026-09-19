using FluentValidation;
using SpaceCraft.Domain.Common;

namespace SpaceCraft.Api.Features.Projects.Requests;

public sealed record RenameProjectRequest(string Name)
{
    public sealed class Validator : AbstractValidator<RenameProjectRequest>
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
