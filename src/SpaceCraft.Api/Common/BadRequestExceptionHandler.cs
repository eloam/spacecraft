using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SpaceCraft.Api.Common;

/// <summary>
/// Reports a malformed request as the client error it is.
///
/// A missing or unreadable body throws <see cref="BadHttpRequestException"/>, which already
/// carries the status it deserves — 400, or 413 for a body over the limit. The default
/// handler ignores that and answers 500, telling the caller the server broke when in fact
/// their request did, and putting a server fault in the logs for every bad payload.
///
/// Registered ahead of the default handler, and declining anything else so genuine faults
/// still surface as 500.
/// </summary>
public sealed class BadRequestExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest) return false;

        httpContext.Response.StatusCode = badRequest.StatusCode;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = badRequest,
            ProblemDetails = new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = "Bad request",
                // Deliberately not the exception's own message: it names the handler's
                // parameter, which is an implementation detail the caller cannot act on.
                Detail = "The request body was missing or could not be read.",
            },
        });
    }
}
