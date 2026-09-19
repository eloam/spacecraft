using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Api.Features.Members.Responses;
using SpaceCraft.Domain.Organizations;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Members;

public static class ListMembers
{
    public static async Task<Results<Ok<List<MemberResponse>>, ProblemHttpResult>> HandleAsync(
        Guid organizationId,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        IOrganizationAccess access,
        CancellationToken ct)
    {
        var caller = principal.Id();

        if (await access.DenyUnlessAsync(organizationId, caller, OrganizationRole.Viewer, ct) is { } denied)
        {
            return denied;
        }

        var members = await MemberQueries.ListAsync(db, organizationId, caller, ct);
        return TypedResults.Ok(members);
    }
}
