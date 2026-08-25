namespace SpaceCraft.Domain.Common;

/// <summary>
/// Identifiers are UUIDv7: random enough to be unguessable in URLs, but time-ordered
/// so Postgres b-tree indexes stay dense instead of fragmenting like they do with v4.
/// </summary>
public static class Ids
{
    public static Guid New() => Guid.CreateVersion7();
}
