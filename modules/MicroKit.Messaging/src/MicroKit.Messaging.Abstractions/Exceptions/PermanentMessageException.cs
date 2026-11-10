namespace MicroKit.Messaging;

/// <summary>
/// Thrown by a handler when the message it was given can never be processed, whatever is retried.
/// </summary>
/// <remarks>
/// <para>
/// <b>Application code raises this one.</b> The engine catches it and dead-letters the message
/// at once, consuming no retries — on the bus and on the inbox path alike. It is the handler's way
/// to say "this message violates the contract", without referencing the SPI namespace.
/// </para>
/// <para>
/// <b>Never throw it for</b> a timeout, a refused connection, an HTTP 503, a database timeout or
/// a deadlock: guessing permanence wrongly loses the message. Let those propagate untyped, which
/// redelivers; throw <see cref="InboxDependencyUnavailableException"/> when a shared dependency
/// is down.
/// </para>
/// </remarks>
public sealed class PermanentMessageException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PermanentMessageException"/> class.</summary>
    public PermanentMessageException()
        : base("The message is permanently unprocessable.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PermanentMessageException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    public PermanentMessageException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PermanentMessageException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The underlying fault.</param>
    public PermanentMessageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
