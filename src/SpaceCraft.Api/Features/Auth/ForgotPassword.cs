using SpaceCraft.Api.Email;
using SpaceCraft.Api.Features.Auth.Responses;
using SpaceCraft.Api.Features.Auth.Requests;
using Microsoft.AspNetCore.Mvc;

namespace SpaceCraft.Api.Features.Auth;

public static class ForgotPassword
{
    public static IResult Handle(
        [FromBody] ForgotPasswordRequest request,
        BackgroundWorkQueue<PasswordResetRequest> resetRequests)
    {
        // That this is an email address is the request's Validator's business.
        var address = request.Email.Trim();

        // Everything past this point — looking the account up, issuing a token, sending the
        // mail — happens on a background worker. The handler must not branch on whether the
        // address is registered, because the time it takes to do so is itself an answer.
        resetRequests.Enqueue(new PasswordResetRequest(address));

        return TypedResults.Accepted(
            (string?)null,
            new AcknowledgementResponse(
                "If that address has an account, a reset link is on its way."));
    }
}
