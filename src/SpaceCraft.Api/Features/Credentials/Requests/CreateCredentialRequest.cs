using FluentValidation;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;

namespace SpaceCraft.Api.Features.Credentials.Requests;

/// <param name="Registry">The kind of registry the token is for.</param>
/// <param name="Label">How the credential is told apart from the others.</param>
/// <param name="Token">The secret itself, stored encrypted and never returned.</param>
/// <param name="Username">
/// Half of the credential wherever a registry wants one — Maven, Nexus, Artifactory, and
/// PyPI where it is the literal "__token__". Absent for npm and NuGet, which authenticate
/// with the token alone.
/// </param>
/// <param name="ExpiresAt">When the registry will stop accepting the token, if known.</param>
/// <param name="RegistryUrl">Where this token publishes. Empty means the public registry for its kind.</param>
public sealed record CreateCredentialRequest(
    RegistryKind Registry,
    string Label,
    string Token,
    string? Username,
    DateTimeOffset? ExpiresAt,
    string? RegistryUrl = null)
{
    public sealed class Validator : AbstractValidator<CreateCredentialRequest>
    {
        // FluentValidation resolves validators from DI, so the host environment arrives by
        // constructor injection like it would in any other service.
        public Validator(IHostEnvironment environment)
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            // Enums bind from any integer, and RegistryEndpoints.DefaultFor throws on one it
            // does not know. Nothing below is judged until the registry is one that exists.
            RuleFor(request => request.Registry)
                .IsInEnum()
                .WithMessage("'{PropertyValue}' is not a registry.")
                .DependentRules(() =>
                {
                    RuleFor(request => request.Label)
                        .NotEmpty()
                        .WithMessage("Give the credential a label so it can be told apart later.");

                    // Two registries have no address worth guessing — Maven Central deploys
                    // through a staging service rather than the mirror it reads from, and
                    // Packagist takes a VCS URL instead of an upload. Asked for it here rather
                    // than letting a credential be created that could only fail at the last
                    // step of a run.
                    RuleFor(request => request.RegistryUrl)
                        .Must((request, _) => RegistryEndpoints.DefaultFor(request.Registry) is not null)
                        .WithMessage(request =>
                            $"{request.Registry} has no address that can be guessed. Give the address "
                            + "that accepts a publish — for Maven, the staging or repository URL your "
                            + "deployment uses.")
                        .When(request => string.IsNullOrWhiteSpace(request.RegistryUrl));

                    // The runner will make this request on the caller's behalf, and its output
                    // is readable back through the step log. Development is exempt because that
                    // is where the registry genuinely is a container on a private network.
                    RuleFor(request => request.RegistryUrl)
                        .Must(url => environment.IsDevelopment() || !RegistryEndpoints.IsInternal(url!.Trim()))
                        .WithMessage(request =>
                            $"'{request.RegistryUrl}' is a private or loopback address. A registry has to "
                            + "be reachable from where the pipeline runs, and this platform does not "
                            + "publish to addresses inside its own network.")
                        .Must(url => RegistryEndpoints.IsValid(url!.Trim()))
                        .WithMessage(request =>
                            $"'{request.RegistryUrl}' is not an absolute http or https URL."
                            + (RegistryEndpoints.DefaultFor(request.Registry) is { } fallback
                                ? $" Leave it empty to publish to {fallback}."
                                : string.Empty))
                        .When(request => !string.IsNullOrWhiteSpace(request.RegistryUrl));

                    // Told apart from an empty field on purpose. Both used to answer "Provide the
                    // registry token", which reads as "you left it blank" to somebody who did type
                    // something, and sends them looking at the form rather than at what they pasted.
                    RuleFor(request => request.Token)
                        .NotEmpty()
                        .WithMessage("Provide the registry token.")
                        // Only npmjs.org issues npm_ tokens. A registry an organization runs mints
                        // its own — Verdaccio hands out a 108-character base64 string, Nexus and
                        // Artifactory their own shapes — so holding those to npm's format refused
                        // the very credentials the endpoint field exists to store.
                        .Must((request, token) => !PublishesToNpmjs(request) || LooksLikeNpmToken(token.Trim()))
                        .WithMessage((_, token) =>
                        {
                            var pasted = token.Trim();
                            return $"'{pasted[..Math.Min(4, pasted.Length)]}…' does not look like an npm token. Use an "
                                + "automation token (npm_...): granular or classic publish tokens with 2FA enabled "
                                + "will fail on an unattended publish.";
                        })
                        // Measured as it will be stored — trimmed.
                        .Must(token => token.Trim().Length >= 8)
                        .WithMessage((_, token) =>
                            $"That token is {token.Trim().Length} characters. A registry token is far longer — "
                            + "check that the whole value was pasted.");
                });
        }

        /// <summary>
        /// Whether the token will be presented to npmjs.org: the address an npm credential gets
        /// when none is given, or the one given when it names the public host.
        /// </summary>
        private static bool PublishesToNpmjs(CreateCredentialRequest request) =>
            request.Registry == RegistryKind.Npm
            && RegistryEndpoints.IsPublicFor(
                RegistryKind.Npm,
                string.IsNullOrWhiteSpace(request.RegistryUrl)
                    ? RegistryEndpoints.DefaultFor(RegistryKind.Npm)
                    : request.RegistryUrl.Trim());

        /// <summary>
        /// npm automation tokens are the only kind that work unattended; classic tokens on a
        /// 2FA-protected account prompt for an OTP that no CI system can answer.
        /// </summary>
        private static bool LooksLikeNpmToken(string token) =>
            token.StartsWith("npm_", StringComparison.Ordinal) || Guid.TryParse(token, out _);
    }
}
