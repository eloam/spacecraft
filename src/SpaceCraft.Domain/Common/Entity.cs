namespace SpaceCraft.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Ids.New();
    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; protected set; } = DateTimeOffset.UtcNow;

    public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
