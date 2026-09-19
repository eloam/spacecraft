using SpaceCraft.Api.Common;
using SpaceCraft.Api.Email;
using SpaceCraft.Api.Features.Waitlist.Responses;
using SpaceCraft.Api.Features.Waitlist.Requests;
using Microsoft.AspNetCore.Mvc;

namespace SpaceCraft.Api.Features.Waitlist;

public static class JoinWaitlist
{
    public static IResult Handle(
        [FromBody] WaitlistSignupRequest request,
        HttpContext http,
        BackgroundWorkQueue<WaitlistSignup> signups)
    {
        var address = request.Email?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(address) || !IsPlausibleEmail(address))
        {
            return ApiResults.Invalid(nameof(request.Email), "Enter a valid email address.");
        }

        // Enqueued unconditionally. Returning the same acknowledgement for a new, pending and
        // confirmed address is not enough on its own: doing the database work inline made the
        // already-confirmed case measurably faster, which answered the question the identical
        // response was there to refuse.
        signups.Enqueue(new WaitlistSignup(
            address,
            request.Source,
            MailedTokens.HashIp(http.Connection.RemoteIpAddress?.ToString())));

        return TypedResults.Ok(new WaitlistSignupResponse(
            "Check your inbox — we've sent you a confirmation link."));
    }

    /// <summary>
    /// Deliberately permissive. Full RFC 5322 validation rejects addresses that work, and
    /// the confirmation email is the real check.
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
