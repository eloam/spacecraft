using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Credentials;

public static class RevokeCredential
{
    public static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid organizationId,
        Guid credentialId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        var credential = await db.RegistryCredentials
            .FirstOrDefaultAsync(c => c.Id == credentialId && c.OrganizationId == organizationId, ct);

        if (credential is null)
        {
            return ApiResults.NotFound("The credential");
        }

        credential.Revoke();
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }
}
