using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Domain.Projects;
using SpaceCraft.Domain.Registries;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features;

public sealed record CreateCredentialRequest(
    RegistryKind Registry,
    string Label,
    string Token,
    DateTimeOffset? ExpiresAt,
    /// <summary>Where this token publishes. Empty means the public registry for its kind.</summary>
    string? RegistryUrl = null);

/// <summary>
/// Deliberately has no token field. There is no endpoint anywhere that returns a stored
/// credential: once written, it is only ever read by the publish step.
/// </summary>
public sealed record CredentialResponse(
    Guid Id,
    RegistryKind Registry,
    string RegistryUrl,
    string Label,
    string LastFour,
    bool IsRevoked,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt,
    /// <summary>How many SDK targets publish with it — what deleting it would break.</summary>
    int TargetsUsing);

public static class CredentialEndpoints
{
    public static RouteGroupBuilder MapCredentialEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations/{organizationId:guid}/credentials")
            .WithTags("Credentials")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .Produces<List<CredentialResponse>>()
            .WithSummary("Lists registry credentials, without their tokens.");

        group.MapPost("/", CreateAsync)
            .Produces<CredentialResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .WithSummary("Stores an encrypted registry token.");

        group.MapPost("/{credentialId:guid}/restore", RestoreAsync)
            .Produces<CredentialResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Undoes a revocation, so the credential can publish again.");

        group.MapDelete("/{credentialId:guid}/permanently", DeleteAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Deletes a credential, unless an SDK target still publishes with it.");

        group.MapDelete("/{credentialId:guid}", RevokeAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Revokes a registry credential.");

        return api;
    }

    private static async Task<IResult> ListAsync(
        Guid organizationId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct))
        {
            return ApiResults.NotFoundOrForbidden();
        }

        var credentials = await db.RegistryCredentials
            .AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .OrderBy(c => c.Registry).ThenByDescending(c => c.CreatedAt)
            .Select(c => new CredentialResponse(
                c.Id,
                c.Registry,
                c.RegistryUrl,
                c.Label,
                c.LastFour,
                c.IsRevoked,
                c.ExpiresAt,
                c.LastUsedAt,
                c.CreatedAt,
                db.SdkTargets.Count(t => t.RegistryCredentialId == c.Id)))
            .ToListAsync(ct);

        return TypedResults.Ok(credentials);
    }

    private static async Task<IResult> CreateAsync(
        Guid organizationId,
        [FromBody] CreateCredentialRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        ISecretProtector protector,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct))
        {
            return ApiResults.NotFoundOrForbidden();
        }

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
        if (request.Registry == RegistryKind.Npm && !LooksLikeNpmToken(token))
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

        if (!string.IsNullOrWhiteSpace(request.RegistryUrl)
            && !RegistryEndpoints.IsValid(request.RegistryUrl.Trim()))
        {
            return ApiResults.Invalid(
                nameof(request.RegistryUrl),
                $"'{request.RegistryUrl}' is not an absolute http or https URL. Leave it empty "
                + $"to publish to {RegistryEndpoints.DefaultFor(request.Registry)}.");
        }

        // Encrypted here and never again held in plaintext outside the publish step.
        var credential = new RegistryCredential(
            organizationId,
            request.Registry,
            request.Label.Trim(),
            protector.Protect(token),
            token[^4..],
            request.RegistryUrl);

        credential.SetExpiry(request.ExpiresAt);

        db.RegistryCredentials.Add(credential);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/organizations/{organizationId}/credentials/{credential.Id}",
            new CredentialResponse(
                credential.Id, credential.Registry, credential.RegistryUrl, credential.Label,
                credential.LastFour, credential.IsRevoked, credential.ExpiresAt,
                credential.LastUsedAt, credential.CreatedAt, TargetsUsing: 0));
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
            (await TargetsUsingAsync(credential.OrganizationId, credential.Id, db, ct)).Count);

    private static async Task<IResult> RestoreAsync(
        Guid organizationId,
        Guid credentialId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct))
        {
            return ApiResults.NotFoundOrForbidden();
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

    /// <summary>
    /// Removes the record for good, which revoking does not.
    ///
    /// <para>
    /// Refused while an SDK target still names it: the endpoint lives on the credential, so
    /// deleting it leaves those targets pointing at nothing and every publish failing with a
    /// message about a credential nobody can look up any more.
    /// </para>
    /// </summary>
    private static async Task<IResult> DeleteAsync(
        Guid organizationId,
        Guid credentialId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct))
        {
            return ApiResults.NotFoundOrForbidden();
        }

        var credential = await db.RegistryCredentials
            .FirstOrDefaultAsync(c => c.Id == credentialId && c.OrganizationId == organizationId, ct);

        if (credential is null)
        {
            return ApiResults.NotFound("The credential");
        }

        var users = await TargetsUsingAsync(organizationId, credentialId, db, ct);

        if (users.Count > 0)
        {
            return ApiResults.Conflict(
                $"{users.Count} SDK target{(users.Count == 1 ? " still publishes" : "s still publish")} with this "
                + $"credential: {string.Join(", ", users)}. Point them elsewhere first, or set "
                + "them not to publish.");
        }

        db.RegistryCredentials.Remove(credential);
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }

    /// <summary>Package names of the targets that publish with a credential, for a message.</summary>
    private static Task<List<string>> TargetsUsingAsync(
        Guid organizationId, Guid credentialId, SpaceCraftDbContext db, CancellationToken ct) =>
        db.SdkTargets
            .AsNoTracking()
            .Where(t => t.RegistryCredentialId == credentialId
                && db.ApiProjects.Any(p => p.Id == t.ApiProjectId && p.OrganizationId == organizationId))
            .Select(t => t.PackageName)
            .OrderBy(name => name)
            .ToListAsync(ct);

    private static async Task<IResult> RevokeAsync(
        Guid organizationId,
        Guid credentialId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        if (!await access.HasAtLeastAsync(organizationId, principal.Id(), OrganizationRole.Admin, ct))
        {
            return ApiResults.NotFoundOrForbidden();
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

    /// <summary>
    /// npm automation tokens are the only kind that work unattended; classic tokens on a
    /// 2FA-protected account prompt for an OTP that no CI system can answer.
    /// </summary>
    private static bool LooksLikeNpmToken(string token) =>
        token.StartsWith("npm_", StringComparison.Ordinal) || Guid.TryParse(token, out _);
}
