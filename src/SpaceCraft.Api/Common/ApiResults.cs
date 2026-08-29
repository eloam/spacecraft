using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace SpaceCraft.Api.Common;

/// <summary>
/// Consistent RFC 9457 problem responses. Endpoints return these rather than raw status
/// codes so the front end can rely on one error shape everywhere.
/// </summary>
public static class ApiResults
{
    public static ProblemHttpResult Problem(string title, string detail, int statusCode) =>
        TypedResults.Problem(new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = statusCode,
        });

    public static ProblemHttpResult NotFound(string what) =>
        Problem("Not found", $"{what} was not found.", StatusCodes.Status404NotFound);

    public static ProblemHttpResult Conflict(string detail) =>
        Problem("Conflict", detail, StatusCodes.Status409Conflict);

    public static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [field] = [message],
        });

    /// <summary>
    /// Used where an organization exists but the caller is not a member: reported as 404
    /// so the API does not confirm that a given organization id is real.
    /// </summary>
    public static ProblemHttpResult NotFoundOrForbidden() =>
        NotFound("The organization");
}
