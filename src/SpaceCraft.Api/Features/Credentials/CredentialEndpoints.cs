using SpaceCraft.Api.Features.Credentials.Responses;

namespace SpaceCraft.Api.Features.Credentials;

public static class CredentialEndpoints
{
    public static RouteGroupBuilder MapCredentialEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations/{organizationId:guid}/credentials")
            .WithTags("Credentials")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/", ListCredentials.HandleAsync)
            .Produces<List<CredentialResponse>>()
            .WithSummary("Lists registry credentials, without their tokens.");

        group.MapPost("/", CreateCredential.HandleAsync)
            .Produces<CredentialResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .WithSummary("Stores an encrypted registry token.");

        group.MapPost("/{credentialId:guid}/restore", RestoreCredential.HandleAsync)
            .Produces<CredentialResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Undoes a revocation, so the credential can publish again.");

        group.MapDelete("/{credentialId:guid}/permanently", DeleteCredential.HandleAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Deletes a credential, unless an SDK target still publishes with it.");

        group.MapDelete("/{credentialId:guid}", RevokeCredential.HandleAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Revokes a registry credential.");

        return api;
    }
}
