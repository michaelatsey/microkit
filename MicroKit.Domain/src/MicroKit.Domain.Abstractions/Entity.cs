
using MicroKit.Domain.Contracts;

namespace MicroKit.Domain.Abstractions;

/// <summary>Base class for all entities. Identity is defined by <see cref="GetKeys"/>.</summary>
public abstract class Entity : IEntity
{
    /// <summary>
    /// Gets the keys.
    /// </summary>
    /// <returns></returns>
    public abstract object[] GetKeys();
}

/// <summary>Base class for entities with a strongly-typed identity key.</summary>
/// <typeparam name="TKey">The type of the primary key.</typeparam>
public abstract class Entity<TKey> : Entity, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    /// Gets the identifier.
    /// </summary>
    /// <value>
    /// The identifier.
    /// </value>
    public virtual TKey Id { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Entity{TKey}"/> class.
    /// </summary>
    protected Entity()
    {
        Id = default!;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Entity{TKey}"/> class.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <exception cref="ArgumentException">Entity Id cannot be the default value. - id</exception>
    protected Entity(TKey id)
    {
        if (EqualityComparer<TKey>.Default.Equals(id, default!))
        {
            throw new ArgumentException("Entity Id cannot be the default value.", nameof(id));
        }
        Id = id;
    }

    /// <summary>
    /// Gets the keys.
    /// </summary>
    /// <returns></returns>
    public override object[] GetKeys() => [Id];

    /// <summary>
    /// Converts to string.
    /// </summary>
    /// <returns>
    /// A <see cref="string" /> that represents this instance.
    /// </returns>
    public override string ToString()
    {
        return $"[ENTITY: {GetType().Name}] Id = {Id}";
    }
}