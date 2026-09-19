using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Organizations.Requests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Organizations;

public static class DeleteOrganization
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        [FromBody] DeleteOrganizationRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        OrganizationDeleter deleter,
        CancellationToken ct)
    {
        // Owner, not Admin: this removes every project, run and credential the organization
        // has, and an admin was added by someone else.
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Owner, ct) is { } denied)
        {
            return denied;
        }

        var organization = await db.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        if (organization is null)
        {
            return ApiResults.NotFoundOrForbidden();
        }

        // Typing the name is the only friction between a stray click and losing everything.
        if (!string.Equals(request.Name?.Trim(), organization.Name, StringComparison.Ordinal))
        {
            return ApiResults.Invalid(
                nameof(request.Name), $"Type the organization's name exactly: {organization.Name}");
        }

        if (await deleter.DeleteAsync(organizationId, ct) is { } refusal)
        {
            return ApiResults.Conflict(refusal.Reason);
        }

        return TypedResults.NoContent();
    }
}
