using SpaceCraft.Api.Common;
using SpaceCraft.Api.Email;
using SpaceCraft.Api.Features.Waitlist.Responses;
using SpaceCraft.Api.Features.Waitlist.Requests;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace SpaceCraft.Api.Features.Waitlist;

public static class JoinWaitlist
{
    public static Results<Ok<WaitlistSignupResponse>, ValidationProblem> Handle(
        [FromBody] WaitlistSignupRequest request,
        HttpContext http,
        BackgroundWorkQueue<WaitlistSignup> signups)
    {
        // Whether this is an address at all is the request's Validator's business.
        var address = request.Email.Trim().ToLowerInvariant();

        // Enqueued unconditionally. Returning the same acknowledgement for a new, pending and
        // confirmed address is not enough on its own: doing the database work inline made the
        // already-confirmed case measurably faster, which answered the question the identical
        // response was there to refuse.
        signups.Enqueue(new WaitlistSignup(
            address,
            request.Source,
            HashIp(http.Connection.RemoteIpAddress?.ToString())));

        return TypedResults.Ok(new WaitlistSignupResponse(
            "Check your inbox — we've sent you a confirmation link."));
    }

    /// <summary>
    /// Stores a hash rather than the address itself: enough to rate-limit repeat abuse,
    /// without retaining a personal identifier for everyone who visits the landing page.
    /// </summary>
    private static string? HashIp(string? ip) =>
        string.IsNullOrWhiteSpace(ip) ? null : OpaqueTokens.Hash(ip);
}
