namespace MicroKit.Domain.Contracts;

/// <summary>
/// 
/// </summary>
/// <seealso cref="IEntity" />
public interface IAggregateRoot : IEntity
{
}

/// <summary>
/// 
/// </summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <seealso cref="IEntity" />
public interface IAggregateRoot<out TKey> : IEntity<TKey>, IAggregateRoot
    where TKey : notnull
{
}