namespace MicroKit.MediatR.Markers;

/// <summary>
/// Opts a command into <c>IdempotencyBehavior</c> (pipeline order 400).
/// Duplicate submissions with the same <see cref="IdempotencyKey"/> return the original
/// result without re-executing the handler.
/// </summary>
/// <example>
/// <code>
/// public sealed record CreateOrderCommand(Guid UserId, OrderItem[] Items)
///     : ICommand&lt;Result&lt;OrderId&gt;&gt;, IIdempotentCommand
/// {
///     public string IdempotencyKey => $"create-order:{UserId}:{ComputeItemsHash(Items)}";
/// }
/// </code>
/// </example>
public interface IIdempotentCommand
{
    /// <summary>
    /// Unique, deterministic key derived from the command's inputs.
    /// Must be non-null and non-empty.
    /// </summary>
    string IdempotencyKey { get; }
}
