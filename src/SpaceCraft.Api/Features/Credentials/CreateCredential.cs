using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Credentials;

public sealed record CreateCredentialRequest(
    RegistryKind Registry,
    string Label,
    string Token,
    /// <summary>
    /// Half of the credential wherever a registry wants one — Maven, Nexus, Artifactory, and
    /// PyPI where it is the literal "__token__". Absent for npm and NuGet, which authenticate
    /// with the token alone.
    /// </summary>
    string? Username,
    DateTimeOffset? ExpiresAt,
    /// <summary>Where this token publishes. Empty means the public registry for its kind.</summary>
    string? RegistryUrl = null);

public static class CreateCredential
{
    public static async Task<IResult> HandleAsync(
        Guid organizationId,
        [FromBody] CreateCredentialRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        ISecretProtector protector,
        IHostEnvironment environment,
        CancellationToken ct)
    {
        if (await access.DenyUnlessAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct) is { } denied)
        {
            return denied;
        }

        var isDevelopment = environment.IsDevelopment();

        if (string.IsNullOrWhiteSpace(request.Label))
        {
            return ApiResults.Invalid(nameof(request.Label), "Give the credential a label so it can be told apart later.");
        }

        var token = request.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return ApiResults.Invalid(nameof(request.Token), "Provide the registry token.");
        }

        // Told apart from an empty field on purpose. Both used to answer "Provide the registry
        // token", which reads as "you left it blank" to somebody who did type something, and
        // sends them looking at the form rather than at what they pasted.
        // Checked before the token, because which endpoint this will be presented to is what
        // decides what the token should look like.
        if (string.IsNullOrWhiteSpace(request.RegistryUrl))
        {
            // Two registries have no address worth guessing — Maven Central deploys through a
            // staging service rather than the mirror it reads from, and Packagist takes a VCS
            // URL instead of an upload. Asked for it here rather than letting a credential be
            // created that could only fail at the last step of a run.
            if (RegistryEndpoints.DefaultFor(request.Registry) is null)
            {
                return ApiResults.Invalid(
                    nameof(request.RegistryUrl),
                    $"{request.Registry} has no address that can be guessed. Give the address "
                    + "that accepts a publish — for Maven, the staging or repository URL your "
                    + "deployment uses.");
            }
        }
        else if (!isDevelopment && RegistryEndpoints.IsInternal(request.RegistryUrl.Trim()))
        {
            // The runner will make this request on the caller's behalf, and its output is
            // readable back through the step log. Development is exempt because that is where
            // the registry genuinely is a container on a private network.
            return ApiResults.Invalid(
                nameof(request.RegistryUrl),
                $"'{request.RegistryUrl}' is a private or loopback address. A registry has to "
                + "be reachable from where the pipeline runs, and this platform does not "
                + "publish to addresses inside its own network.");
        }
        else if (!RegistryEndpoints.IsValid(request.RegistryUrl.Trim()))
        {
            return ApiResults.Invalid(
                nameof(request.RegistryUrl),
                $"'{request.RegistryUrl}' is not an absolute http or https URL."
                + (RegistryEndpoints.DefaultFor(request.Registry) is { } fallback
                    ? $" Leave it empty to publish to {fallback}."
                    : string.Empty));
        }

        // Only npmjs.org issues npm_ tokens. A registry an organization runs mints its own —
        // Verdaccio hands out a 108-character base64 string, Nexus and Artifactory their own
        // shapes — so holding those to npm's format refused the very credentials the endpoint
        // field exists to store.
        if (request.Registry == RegistryKind.Npm
            && RegistryEndpoints.IsPublicFor(RegistryKind.Npm, request.RegistryUrl?.Trim() ?? RegistryEndpoints.DefaultFor(RegistryKind.Npm))
            && !LooksLikeNpmToken(token))
        {
            return ApiResults.Invalid(
                nameof(request.Token),
                $"'{token[..Math.Min(4, token.Length)]}…' does not look like an npm token. Use an "
                + "automation token (npm_...): granular or classic publish tokens with 2FA enabled "
                + "will fail on an unattended publish.");
        }

        if (token.Length < 8)
        {
            return ApiResults.Invalid(
                nameof(request.Token),
                $"That token is {token.Length} characters. A registry token is far longer — "
                + "check that the whole value was pasted.");
        }

        // Encrypted here and never again held in plaintext outside the publish step. Stored
        // as a document rather than a bare token: a registry that wants a user with its
        // secret must not require a column, and so a migration over encrypted rows.
        var secret = new RegistrySecret(token, string.IsNullOrWhiteSpace(request.Username)
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

    /// <summary>
    /// npm automation tokens are the only kind that work unattended; classic tokens on a
    /// 2FA-protected account prompt for an OTP that no CI system can answer.
    /// </summary>
    private static bool LooksLikeNpmToken(string token) =>
        token.StartsWith("npm_", StringComparison.Ordinal) || Guid.TryParse(token, out _);
}
