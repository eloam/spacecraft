using FluentValidation;

namespace SpaceCraft.Api.Features.Members.Requests;

public sealed record AcceptInvitationRequest(string Token)
{
    public sealed class Validator : AbstractValidator<AcceptInvitationRequest>
    {
        public Validator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            RuleFor(request => request.Token)
                .NotEmpty()
                .WithMessage("An invitation token is required.");
        }
    }
}
