using FluentValidation;

namespace SpaceCraft.Api.Features.Auth.Requests;

public sealed record ForgotPasswordRequest(string Email)
{
    public sealed class Validator : AbstractValidator<ForgotPasswordRequest>
    {
        public Validator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            // Judged trimmed, as the handler enqueues it, and asked only for an '@': the
            // account lookup happens on a background worker precisely so that this endpoint
            // says nothing about which addresses exist. One message for both failures, since
            // the fix is the same.
            RuleFor(request => request.Email)
                .NotEmpty()
                .WithMessage("Enter a valid email address.")
                .Must(email => email.Trim().Contains('@', StringComparison.Ordinal))
                .WithMessage("Enter a valid email address.");
        }
    }
}
