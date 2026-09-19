using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Common;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Notifications.Responses;

namespace SpaceCraft.Api.Features.Notifications;

public static class GetPreferences
{
    public static async Task<Ok<List<NotificationPreferenceState>>> HandleAsync(
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        var stated = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == principal.Id()).ToListAsync(ct);
        return TypedResults.Ok(NotificationPreferenceState.Matrix(stated));
    }
}
