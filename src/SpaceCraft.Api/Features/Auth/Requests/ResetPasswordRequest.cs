using FluentValidation;

namespace SpaceCraft.Api.Features.Auth.Requests;

public sealed record ResetPasswordRequest(string Token, string NewPassword)
{
    public sealed class Validator : AbstractValidator<ResetPasswordRequest>
    {
        public Validator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            RuleFor(request => request.Token)
                .NotEmpty()
                .WithMessage("The reset link is incomplete.");

            // Identity's password policy judges the password itself, once the token has been
            // found redeemable; this only refuses an empty one before either is looked at.
            RuleFor(request => request.NewPassword)
                .NotEmpty()
                .WithMessage("Choose a new password.");
        }
    }
}
