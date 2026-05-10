namespace MicroKit.Domain.Abstractions;

/// <summary>
/// 
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
/// 
/// </summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <seealso cref="AggregateRootBase" />
[Serializable]
public abstract class AggregateRoot<TKey> : AggregateRootBase<TKey>
    where TKey : notnull
{

    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateRoot{TKey}"/> class.
    /// </summary>
    protected AggregateRoot() { }
    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateRoot{TKey}"/> class.
    /// </summary>
    /// <param name="id">The identifier.</param>
    protected AggregateRoot(TKey id)
        : base(id) { }


}
