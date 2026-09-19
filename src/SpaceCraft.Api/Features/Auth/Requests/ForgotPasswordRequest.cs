using FluentValidation;

namespace SpaceCraft.Api.Features.Auth.Requests;

public sealed record ForgotPasswordRequest(string Email)
{
    public sealed class Validator : AbstractValidator<ForgotPasswordRequest>
    {
        public Validator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            // One message for both: whether the address is missing or malformed, the fix is
            // the same, and the handler never said more.
            RuleFor(request => request.Email)
                .NotEmpty()
                .WithMessage("Enter a valid email address.")
                .EmailAddress()
                .WithMessage("Enter a valid email address.");
        }
    }
}
