using SpaceCraft.Infrastructure.Persistence;
using SpaceCraft.Api.Features.Members.Responses;
using Microsoft.EntityFrameworkCore;

namespace SpaceCraft.Api.Features.Members;

internal static class MemberQueries
{
    /// <summary>
    /// Members joined to their accounts. Identity lives in its own tables, so the name and
    /// address are not on the membership row.
    /// </summary>
    public static async Task<List<MemberResponse>> ListAsync(
        SpaceCraftDbContext db, Guid organizationId, Guid caller, CancellationToken ct)
    {
        var rows =
            from member in db.OrganizationMembers.AsNoTracking()
            where member.OrganizationId == organizationId
            join user in db.Users.AsNoTracking() on member.UserId equals user.Id
            select new { member, user };

        var loaded = await rows.ToListAsync(ct);

        return loaded
            .Select(row => new MemberResponse(
                row.member.UserId,
                row.user.Email ?? string.Empty,
                row.user.DisplayName,
                row.member.Role,
                row.member.UserId == caller,
                row.member.CreatedAt))
            .OrderByDescending(m => m.Role)
            .ThenBy(m => m.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task<Person> DescribeAsync(
        SpaceCraftDbContext db, Guid userId, CancellationToken ct) =>
        await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new Person(u.DisplayName, u.Email ?? ""))
            .FirstOrDefaultAsync(ct) ?? new Person(null, "an account that no longer exists");
}
