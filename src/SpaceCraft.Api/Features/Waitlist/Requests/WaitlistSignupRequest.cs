using FluentValidation;

namespace SpaceCraft.Api.Features.Waitlist.Requests;

public sealed record WaitlistSignupRequest(string Email, string? Source)
{
    public sealed class Validator : AbstractValidator<WaitlistSignupRequest>
    {
        public Validator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            // Judged trimmed, as the handler stores it. One message for both failures: the
            // fix is the same.
            RuleFor(request => request.Email)
                .NotEmpty()
                .WithMessage("Enter a valid email address.")
                .Must(email => IsPlausibleEmail(email.Trim()))
                .WithMessage("Enter a valid email address.");
        }

        /// <summary>
        /// Deliberately permissive: one @, neither first nor last, a dot after it, no spaces,
        /// and no longer than the column. Full RFC 5322 validation rejects addresses that
        /// work, and the confirmation email is the real check.
        /// </summary>
        private static bool IsPlausibleEmail(string value)
        {
            var at = value.IndexOf('@', StringComparison.Ordinal);
            return at > 0
                && at < value.Length - 1
                && value.Length <= 320
                && value.IndexOf('@', at + 1) < 0
                && value.LastIndexOf('.') > at
                && !value.Contains(' ', StringComparison.Ordinal);
        }
    }
}
