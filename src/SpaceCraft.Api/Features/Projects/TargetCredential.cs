using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Projects;

internal static class TargetCredential
{
    /// <summary>
    /// Refuses a credential a target could not publish with: one the organization does not
    /// have, one for another registry, or one that has been revoked.
    /// </summary>
    public static async Task<IResult?> RejectUnusableAsync(
        Guid organizationId,
        RegistryKind registry,
        Guid? credentialId,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        if (credentialId is not { } id)
        {
            return null;
        }

        var credential = await db.RegistryCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == organizationId, ct);

        if (credential is null)
        {
            return ApiResults.Invalid("registryCredentialId", "That credential does not exist.");
        }

        if (credential.Registry != registry)
        {
            return ApiResults.Invalid(
                "registryCredentialId",
                $"That credential publishes to {credential.Registry}, not {registry}.");
        }

        return credential.IsRevoked
            ? ApiResults.Invalid("registryCredentialId", "That credential has been revoked.")
            : null;
    }
}
