using MicroKit.Idempotency.Abstractions.Models;

namespace MicroKit.Idempotency.Abstractions.Contracts;

/// <summary>
/// Provides context for the current idempotency operation with AsyncLocal support
/// for maintaining context across async flows
/// </summary>
public interface IIdempotencyContext
{
    /// <summary>
    /// Gets the current idempotency key for the executing operation
    /// </summary>
    string? CurrentKey { get; }

    /// <summary>
    /// Gets whether the current operation is idempotent (has a valid idempotency key)
    /// </summary>
    bool IsIdempotent { get; }

    /// <summary>
    /// Gets the status of the current idempotency operation if available
    /// </summary>
    IdempotencyStatus? CurrentStatus { get; }

    /// <summary>
    /// Begins a new idempotency scope for the specified key.
    /// The scope should be disposed when the operation completes.
    /// </summary>
    /// <param name="key">The idempotency key for the scope</param>
    /// <returns>A disposable that ends the scope when disposed</returns>
    /// <exception cref="InvalidOperationException">Thrown when a scope is already active</exception>
    IDisposable BeginScope(string key);
}
