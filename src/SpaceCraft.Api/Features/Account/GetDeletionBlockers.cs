using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Account;

public static class GetDeletionBlockers
{
    public static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct) =>
        TypedResults.Ok(await AccountQueries.DeletionBlockersAsync(db, principal.Id(), ct));
}
