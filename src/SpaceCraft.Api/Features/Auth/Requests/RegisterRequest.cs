using FluentValidation;

namespace SpaceCraft.Api.Features.Auth.Requests;

public sealed record RegisterRequest(string Email, string Password, string? DisplayName)
{
    public sealed class Validator : AbstractValidator<RegisterRequest>
    {
        public Validator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            RuleFor(request => request.Email)
                .NotEmpty()
                .WithMessage("An email address is required.");

            // Identity's password policy judges the password itself; this only keeps a null
            // one from reaching UserManager.CreateAsync, which throws on it.
            RuleFor(request => request.Password)
                .NotEmpty()
                .WithMessage("A password is required.");

            // The same rule as UpdateProfileRequest, since Register stores the same column:
            // measured trimmed, and absent is fine — the name is optional at registration.
            RuleFor(request => request.DisplayName)
                .Must(name => name is null || name.Trim().Length <= 100)
                .WithMessage("Use 100 characters or fewer.");
        }
    }
}
