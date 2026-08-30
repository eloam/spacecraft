using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SpaceCraft.Domain.Organizations;

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

    /// <summary>
    /// Used where the caller <em>is</em> a member but their role is too low, which is a
    /// different answer from <see cref="NotFoundOrForbidden"/> and deserves a different one.
    /// <para>
    /// The 404 mask exists to keep the API from confirming that an organization id is real.
    /// Against a member that purpose is already spent — they are looking at the organization
    /// — so the mask conceals nothing and costs a true answer: it told a viewer clicking
    /// Download that the organization they were reading did not exist. Naming the role
    /// reveals only the permission model, which is documented.
    /// </para>
    /// </summary>
    public static ProblemHttpResult Forbidden(OrganizationRole required) =>
        Problem(
            "Forbidden",
            $"This action requires the {required} role.",
            StatusCodes.Status403Forbidden);
}
