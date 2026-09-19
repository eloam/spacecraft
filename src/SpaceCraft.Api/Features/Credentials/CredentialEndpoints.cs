namespace SpaceCraft.Api.Features.Credentials;

public static class CredentialEndpoints
{
    public static RouteGroupBuilder MapCredentialEndpoints(this RouteGroupBuilder api)
    {
        // The guard's 404 to a non-member, once for the group: every route below opens with
        // it. Its 403 is declared by the routes that ask for more than Viewer, since a read a
        // Viewer may make cannot answer it.
        var group = api.MapGroup("/organizations/{organizationId:guid}/credentials")
            .WithTags("Credentials")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListCredentials.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Lists registry credentials, without their tokens.");

        group.MapPost("/", CreateCredential.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Stores an encrypted registry token.");

        group.MapPost("/{credentialId:guid}/restore", RestoreCredential.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Undoes a revocation, so the credential can publish again.");

        group.MapDelete("/{credentialId:guid}/permanently", DeleteCredential.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Deletes a credential, unless an SDK target still publishes with it.");

        group.MapDelete("/{credentialId:guid}", RevokeCredential.HandleAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Revokes a registry credential.");

        return api;
    }
}
