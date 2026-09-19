using FluentValidation;

namespace SpaceCraft.Api.Features.Waitlist.Requests;

public sealed record WaitlistSignupRequest(string Email, string? Source)
{
    public sealed class Validator : AbstractValidator<WaitlistSignupRequest>
    {
        public Validator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            // Deliberately permissive: one @, neither first nor last. Full RFC 5322 validation
            // rejects addresses that work, and the confirmation email is the real check.
            RuleFor(request => request.Email)
                .NotEmpty()
                .WithMessage("Enter a valid email address.")
                .EmailAddress()
                .WithMessage("Enter a valid email address.");
        }
    }
}
