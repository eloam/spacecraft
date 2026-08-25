using Microsoft.AspNetCore.Identity;

namespace SpaceCraft.Infrastructure.Identity;

/// <summary>
/// Identity user keyed by Guid so it lines up with the rest of the schema.
/// Organization membership is deliberately *not* modelled as Identity roles: roles here
/// are per-organization, and Identity roles are global.
/// </summary>
public sealed class SpaceCraftUser : IdentityUser<Guid>
{
    public SpaceCraftUser()
    {
        Id = Domain.Common.Ids.New();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>GitHub account id, once the user has linked one for Auto-Sync.</summary>
    public string? GitHubUserId { get; set; }
}

public sealed class SpaceCraftRole : IdentityRole<Guid>
{
    public SpaceCraftRole()
    {
        Id = Domain.Common.Ids.New();
    }

    public SpaceCraftRole(string name) : this() => Name = name;
}
