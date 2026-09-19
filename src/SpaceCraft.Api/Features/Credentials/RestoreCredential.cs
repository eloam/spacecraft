using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Credentials;

public static class RestoreCredential
{
    public static async Task<IResult> HandleAsync(
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

        credential.Restore();
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(await ToResponseAsync(credential, db, ct));
    }

    private static async Task<CredentialResponse> ToResponseAsync(
        RegistryCredential credential, SpaceCraftDbContext db, CancellationToken ct) =>
        new(credential.Id,
            credential.Registry,
            credential.RegistryUrl,
            credential.Label,
            credential.LastFour,
            credential.IsRevoked,
            credential.ExpiresAt,
            credential.LastUsedAt,
            credential.CreatedAt,
            await CredentialQueries.UsedByAsync(credential.OrganizationId, credential.Id, db, ct));
}
