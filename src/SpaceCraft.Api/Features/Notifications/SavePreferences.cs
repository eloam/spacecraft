using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaceCraft.Api.Common;
using SpaceCraft.Domain.Notifications;
using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Notifications.Responses;
using SpaceCraft.Api.Features.Notifications.Requests;

namespace SpaceCraft.Api.Features.Notifications;

/// <summary>
/// Replaces the whole matrix.
/// </summary>
/// <remarks>
/// The page sends every switch it shows, so a partial save cannot leave the settings
/// half-applied — and anything the request does not mention keeps whatever it had, which
/// is what lets a future channel ship without every account having to re-save.
/// </remarks>
public static class SavePreferences
{
    public static async Task<Results<Ok<List<NotificationPreferenceState>>, ValidationProblem>> HandleAsync(
        [FromBody] NotificationPreferencesRequest request,
        ClaimsPrincipal principal,
        SpaceCraftDbContext db,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Keyed rather than listed, so a payload naming the same switch twice settles on its
        // last word instead of trying to write two rows the unique index will not take — which
        // reached the caller as a 500 on a request that was merely repetitive.
        var parsed = new Dictionary<(NotificationKind Kind, NotificationChannel Channel), bool>();

        // Whether each name is one we know is the request's Validator's business; here they parse.
        foreach (var state in request.Preferences ?? [])
        {
            var kind = Enum.Parse<NotificationKind>(state.Kind, ignoreCase: true);
            var channel = Enum.Parse<NotificationChannel>(state.Channel, ignoreCase: true);

            parsed[(kind, channel)] = state.Enabled;
        }

        var userId = principal.Id();
        var existing = await db.NotificationPreferences.Where(p => p.UserId == userId).ToListAsync(ct);

        foreach (var ((kind, channel), enabled) in parsed)
        {
            var row = existing.FirstOrDefault(p => p.Kind == kind && p.Channel == channel);

            if (row is null)
            {
                db.NotificationPreferences.Add(new NotificationPreference(userId, kind, channel, enabled));
            }
            else
            {
                row.Set(enabled);
            }
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(NotificationPreferenceState.Matrix(await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == userId).ToListAsync(ct)));
    }
}
