using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Security;
using SpaceCraft.Api.Features.Credentials.Responses;
using SpaceCraft.Api.Features.Credentials.Requests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Credentials;

public static class CreateCredential
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        [FromBody] CreateCredentialRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        ISecretProtector protector,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        // The request's shape — registry, label, token, address — is the request's Validator's business.

        // Encrypted here and never again held in plaintext outside the publish step. Stored
        // as a document rather than a bare token: a registry that wants a user with its
        // secret must not require a column, and so a migration over encrypted rows.
        var secret = new RegistrySecret(request.Token.Trim(), string.IsNullOrWhiteSpace(request.Username)
            ? null
            : request.Username.Trim());

        var credential = new RegistryCredential(
            organizationId,
            request.Registry,
            request.Label.Trim(),
            protector.Protect(secret.ToJson()),
            secret.LastFour(),
            request.RegistryUrl);

        credential.SetExpiry(request.ExpiresAt);

        db.RegistryCredentials.Add(credential);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/organizations/{organizationId}/credentials/{credential.Id}",
            new CredentialResponse(
                credential.Id, credential.Registry, credential.RegistryUrl, credential.Label,
                credential.LastFour, credential.IsRevoked, credential.ExpiresAt,
                credential.LastUsedAt, credential.CreatedAt, UsedBy: []));
    }
}
