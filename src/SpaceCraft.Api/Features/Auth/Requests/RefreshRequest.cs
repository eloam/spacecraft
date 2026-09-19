using FluentValidation;
using SpaceCraft.Domain.Identity;

namespace SpaceCraft.Api.Features.Auth.Requests;

public sealed record RefreshRequest(string RefreshToken)
{
    public sealed class Validator : AbstractValidator<RefreshRequest>
    {
        public Validator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            RuleFor(request => request.RefreshToken)
                .NotEmpty()
                .WithMessage("A refresh token is required.");
        }
    }
}
