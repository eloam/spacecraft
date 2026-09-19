using FluentValidation;

namespace SpaceCraft.Api.Features.Account.Requests;

public sealed record UpdateProfileRequest(string? DisplayName)
{
    public sealed class Validator : AbstractValidator<UpdateProfileRequest>
    {
        public Validator()
        {
            RuleLevelCascadeMode = CascadeMode.Stop;

            // Measured as it will be stored — trimmed. No name at all is a valid request: it
            // clears the one on file.
            RuleFor(request => request.DisplayName)
                .Must(name => name is null || name.Trim().Length <= 100)
                .WithMessage("Use 100 characters or fewer.");
        }
    }
}
