namespace MicroKit.Persistence;

/// <summary>
/// The cause of an optimistic concurrency conflict that a repository detects itself, rather than the
/// database: the version a command expects is not the aggregate's. It is thrown as the
/// <see cref="Exception.InnerException"/> of a <see cref="PersistenceException"/>, and
/// <see cref="IResultUnitOfWork"/> converts it to <see cref="PersistenceErrors.Concurrency"/>.
/// </summary>
/// <remarks>
/// Two places throw it: <c>EfRepository.FindAsync(id, expectedVersion)</c> on a staged root not inserted
/// yet, whose version is 0, and the in-memory double, which checks the expected version at the find. A
/// conflict that the database detects stays a <c>DbUpdateConcurrencyException</c>.
/// It derives from <see cref="InvalidOperationException"/>, the type these inner exceptions had before.
/// A custom repository that checks a version itself throws it the same way, wrapped in a
/// <see cref="PersistenceException"/>, to be converted to <c>CONCURRENCY</c>:
/// <code>
/// throw new PersistenceException("Optimistic concurrency conflict.",
///     new AggregateVersionConflictException("Order 42 is at version 3, not 2.") { ErrorName = "ORDER" });
/// </code>
/// </remarks>
public sealed class AggregateVersionConflictException : InvalidOperationException
{
    /// <summary>Initializes a new instance with a default message and no error name.</summary>
    public AggregateVersionConflictException()
        : base("The expected version of the aggregate does not match.")
    {
    }

    /// <summary>Initializes a new instance with a message and no error name.</summary>
    /// <param name="message">A message describing the conflict.</param>
    public AggregateVersionConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message, the exception that caused it and no error name.</summary>
    /// <param name="message">A message describing the conflict.</param>
    /// <param name="innerException">The exception that caused the conflict.</param>
    public AggregateVersionConflictException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the error name of the aggregate root, the <c>&lt;ROOT&gt;</c> segment of the code, or
    /// <see langword="null"/> when it is unknown (the code is then <c>PERSISTENCE.CONCURRENCY</c>).
    /// </summary>
    /// <exception cref="ArgumentException">Set to a value that is not a valid error name (<see cref="PersistenceErrors.IsValidErrorName"/>).</exception>
    public string? ErrorName
    {
        get;
        init => field = value is null || PersistenceErrors.IsValidErrorName(value)
            ? value
            : throw new ArgumentException($"'{value}' is not a valid error name.", nameof(value));
    }
}
