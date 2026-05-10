namespace MicroKit.Domain.Contracts;

/// <summary>
/// 
/// </summary>
public interface IEntity
{
    /// <summary>
    /// Gets the keys.
    /// </summary>
    /// <returns></returns>
    object[] GetKeys();
}

/// <summary>
/// 
/// </summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
public interface IEntity<out TKey> : IEntity
    where TKey : notnull
{
    /// <summary>
    /// Gets the identifier.
    /// </summary>
    /// <value>
    /// The identifier.
    /// </value>
    TKey Id { get; }
}