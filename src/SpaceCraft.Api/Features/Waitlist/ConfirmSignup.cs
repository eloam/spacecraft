using SpaceCraft.Api.Common;
using SpaceCraft.Api.Email;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Waitlist.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Waitlist;

public static class ConfirmSignup
{
    public static async Task<IResult> HandleAsync(
        [FromQuery] string token,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return ApiResults.Invalid(nameof(token), "A confirmation token is required.");
        }

        var hash = MailedTokens.Hash(token);
        var entry = await db.WaitlistEntries.FirstOrDefaultAsync(w => w.ConfirmationTokenHash == hash, ct);

        if (entry is null)
        {
            return ApiResults.Problem(
                "Invalid link",
                "This confirmation link is not valid. Request a new one from the signup form.",
                StatusCodes.Status400BadRequest);
        }

        entry.Confirm(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new WaitlistSignupResponse("You're on the list. We'll be in touch at launch."));
    }
}
