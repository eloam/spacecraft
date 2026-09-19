using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Credentials;

/// <summary>
/// Removes the record for good, which revoking does not.
///
/// <para>
/// Refused while an SDK target still names it: the endpoint lives on the credential, so
/// deleting it leaves those targets pointing at nothing and every publish failing with a
/// message about a credential nobody can look up any more.
/// </para>
/// </summary>
public static class DeleteCredential
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

        var users = await CredentialQueries.UsedByAsync(organizationId, credentialId, db, ct);

        if (users.Count > 0)
        {
            // Named with their project: two projects can publish packages of the same name to
            // different registries, so the package alone does not say where to go and fix it.
            var named = users.Select(u => $"{u.PackageName} ({u.ProjectName})");

            return ApiResults.Conflict(
                $"{users.Count} SDK target{(users.Count == 1 ? " still publishes" : "s still publish")} "
                + $"with this credential: {string.Join(", ", named)}. Point them elsewhere first, "
                + "or set them not to publish.");
        }

        db.RegistryCredentials.Remove(credential);
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }
}
