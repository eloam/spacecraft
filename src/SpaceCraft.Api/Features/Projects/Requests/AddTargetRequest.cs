using FluentValidation;
using SpaceCraft.Domain.Projects;

namespace SpaceCraft.Api.Features.Projects.Requests;

public sealed record AddTargetRequest(
    SdkLanguage Language,
    string PackageName,
    RegistryKind? Registry,
    bool AutoPublish,
    /// <summary>Which credential publishes it. Null while the organization has only one.</summary>
    Guid? RegistryCredentialId = null)
{
    public sealed class Validator : AbstractValidator<AddTargetRequest>
    {
        public Validator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            // The domain models six languages; only the ones with a working generator, builder
            // and publisher are accepted, so a user cannot queue a run that can never succeed.
            RuleFor(request => request.Language)
                .Must(language => language.IsSupported())
                .WithMessage(request => $"{request.Language} is not available yet. TypeScript is currently supported.")
                // The rules below ask the language for its registry, which an unsupported one
                // may not have — Go throws — so they only run once the language is accepted.
                .DependentRules(() =>
                {
                    // A caller may name the registry, and nothing checked it belonged to the
                    // language. A Java target with Registry=Npm resolved both seams
                    // successfully — the Java generator built a .jar and the npm publisher was
                    // handed it — so the pairing is refused here rather than discovered at the
                    // last step of a run.
                    RuleFor(request => request.Registry)
                        .Must((request, registry) => registry == request.Language.DefaultRegistry())
                        .WithMessage(request =>
                            $"{request.Language} publishes to {request.Language.DefaultRegistry()}, not to "
                            + $"{request.Registry}. Point the credential at your own {request.Language.DefaultRegistry()} "
                            + "server if it is not the public one.")
                        .When(request => request.Registry is not null);

                    // Judged against the language's registry, which is the only one the rule
                    // above lets through. Trimmed here because the handler trims for persistence.
                    RuleFor(request => request.PackageName)
                        .Must((request, packageName) =>
                            PackageCoordinates.IsValidFor(request.Language.DefaultRegistry(), packageName?.Trim()))
                        .WithMessage(request =>
                            $"'{request.PackageName?.Trim()}' is not a valid package name for {request.Language.DefaultRegistry()}.");

                    RuleFor(request => request.AutoPublish)
                        .Must((request, autoPublish) => !autoPublish || request.RegistryCredentialId is not null)
                        .WithMessage(
                            "A target that publishes nowhere cannot publish automatically. Choose a "
                            + "credential, or leave automatic publishing off.");
                });
        }
    }
}
