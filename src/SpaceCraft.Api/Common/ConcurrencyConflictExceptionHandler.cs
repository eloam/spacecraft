using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Common;

/// <summary>
/// Answers 409 when a save found the row changed by someone else since the handler read it.
///
/// Rows with a concurrency token — runs, organizations, the Identity user — are read, judged
/// and written by a handler in three steps, and a second request can land between the first and the last: a
/// worker renewing a lease while a member cancels the run, two owners leaving at the same
/// instant. The database then refuses the second write with
/// <see cref="DbUpdateConcurrencyException"/>, which is right, and the default handler would
/// report it as 500, which is not: nothing broke, the caller's picture is stale, and the
/// remedy is theirs — reload and try again. Said once here rather than in every handler
/// that writes such a row, so a handler that forgets is still answered correctly.
///
/// Registered ahead of the default handler, and declining anything else so genuine faults
/// still surface as 500.
/// </summary>
public sealed class ConcurrencyConflictExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException conflict) return false;

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = conflict,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = "Someone else changed this at the same time. Reload and try again.",
            },
        });
    }
}
