using FluentValidation;

namespace SpaceCraft.Api.Features.Projects.Requests;

/// <summary>
/// What a target can still be changed to. Language and registry are not here: they decide
/// which generator runs and where the result goes, so changing either would make the row
/// describe something that was never built.
/// </summary>
public sealed record UpdateTargetRequest(
    string PackageName,
    bool AutoPublish,
    Guid? RegistryCredentialId = null)
{
    public sealed class Validator : AbstractValidator<UpdateTargetRequest>
    {
        public Validator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            // PackageName is not judged here: its rule depends on the stored target's
            // registry, which only the handler can see.
            RuleFor(request => request.AutoPublish)
                .Must((request, autoPublish) => !autoPublish || request.RegistryCredentialId is not null)
                .WithMessage(
                    "A target that publishes nowhere cannot publish automatically. Choose a "
                    + "credential, or leave automatic publishing off.");
        }
    }
}
