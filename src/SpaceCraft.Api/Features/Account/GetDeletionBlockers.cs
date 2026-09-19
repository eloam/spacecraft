using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Api.Features.Account.Responses;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Account;

public static class GetDeletionBlockers
{
    public static async Task<Ok<IReadOnlyList<DeletionBlocker>>> HandleAsync(
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct) =>
        TypedResults.Ok(await AccountQueries.DeletionBlockersAsync(db, principal.Id(), ct));
}
