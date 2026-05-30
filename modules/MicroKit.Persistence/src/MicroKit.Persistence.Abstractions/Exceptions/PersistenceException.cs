namespace MicroKit.Persistence.Abstractions;

/// <summary>
/// Thrown by repository and Unit of Work implementations when the underlying
/// persistence provider encounters an unrecoverable error.
/// </summary>
/// <remarks>
/// Common causes: database connection failures, unique constraint violations,
/// optimistic concurrency conflicts, and transaction rollback failures.
/// The provider's original exception is always preserved as
/// <see cref="Exception.InnerException"/> to retain the full diagnostic stack.
/// </remarks>
public sealed class PersistenceException : Exception
{
    /// <summary>
    /// Initializes a new instance of <see cref="PersistenceException"/> with a
    /// descriptive message.
    /// </summary>
    /// <param name="message">A message describing the persistence failure.</param>
    public PersistenceException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance of <see cref="PersistenceException"/> with a
    /// descriptive message and the provider exception that caused the failure.
    /// </summary>
    /// <param name="message">A message describing the persistence failure.</param>
    /// <param name="innerException">The provider exception that caused this failure.</param>
    public PersistenceException(string message, Exception? innerException)
        : base(message, innerException) { }
}
