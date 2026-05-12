namespace MicroKit.Domain.Abstractions;

/// <summary>
/// Convenience base class for aggregate roots without a typed key.
/// Delegates all domain event management and versioning to <see cref="AggregateRootBase"/>.
/// </summary>
/// <seealso cref="AggregateRootBase" />
[Serializable]
public abstract class AggregateRoot : AggregateRootBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateRoot"/> class.
    /// </summary>
    protected AggregateRoot() { }
}

/// <summary>
/// Convenience base class for aggregate roots with a strongly-typed identity key.
/// Delegates all domain event management and versioning to <see cref="AggregateRootBase{TKey}"/>.
/// </summary>
/// <typeparam name="TKey">The type of the primary key.</typeparam>
/// <seealso cref="AggregateRootBase{TKey}" />
[Serializable]
public abstract class AggregateRoot<TKey> : AggregateRootBase<TKey>
    where TKey : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateRoot{TKey}"/> class.
    /// </summary>
    protected AggregateRoot() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateRoot{TKey}"/> class with the specified key.
    /// </summary>
    /// <param name="id">The identifier.</param>
    protected AggregateRoot(TKey id)
        : base(id) { }
}
