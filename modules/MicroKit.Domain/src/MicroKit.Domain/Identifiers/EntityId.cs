namespace MicroKit.Domain.Identifiers;

/// <summary>
/// Base implementation for strongly-typed entity identifiers using Guid values.
/// Provides common functionality like equality, hashing, and string representation.
/// Inherit from this to create specific ID types like OrderId : EntityId&lt;OrderId&gt;.
/// </summary>
/// <typeparam name="T">The concrete identifier type for type safety</typeparam>
public abstract record EntityId<T> : IEntityId where T : EntityId<T>
{
    public Guid Value { get; }

    protected EntityId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Entity identifier cannot be empty.", nameof(value));
        Value = value;
    }

    object IEntityId.Value => Value;

    public override string ToString() => Value.ToString();
}