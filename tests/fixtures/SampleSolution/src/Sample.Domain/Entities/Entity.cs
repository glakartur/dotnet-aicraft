namespace Sample.Domain.Entities;

/// <summary>
/// Base entity used to test inheritance and XML documentation output.
/// </summary>
public abstract class Entity<TId>
{
    protected Entity(TId id)
    {
        Id = id;
    }

    public TId Id { get; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
