using System.Security.Claims;
using SpaceCraft.Api.Common;
using SpaceCraft.Api.Features.Auth;
using SpaceCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Account;

public static class RevokeOtherSessions
{
    public static async Task<NoContent> HandleAsync(
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        await Sessions.RevokeAsync(db, principal.Id(), except: principal.SessionId(), ct);
        return TypedResults.NoContent();
    }
}
