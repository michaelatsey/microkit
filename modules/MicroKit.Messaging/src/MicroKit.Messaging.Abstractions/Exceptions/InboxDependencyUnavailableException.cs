namespace MicroKit.Messaging;

/// <summary>
/// Thrown by a handler when a dependency the entire batch relies on is unreachable, making
/// every remaining row equally unprocessable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Application code raises this one.</b> It is part of the application surface, not the SPI:
/// nothing inside the module ever throws it, and an <see cref="IMessageHandler{T}"/>
/// implementation is its only source. The engine catches it and draws a verdict from it, which
/// is the opposite direction from the exceptions in <c>MicroKit.Messaging.Spi</c>.
/// </para>
/// <para>
/// The processor abandons the batch, releases every unattempted row back to <c>Received</c>,
/// and consumes no retry budget. Turning a five-minute outage into a hundred rows pushed one
/// step closer to dead-lettering is the failure mode this prevents.
/// </para>
/// <para>
/// <b>Throw for:</b> a downstream service that is down, connection refused, authentication
/// rejected — anything where handling the next row is certain to fail too.
/// </para>
/// <para>
/// <b>Do not throw for</b> a single message failing while the dependency is healthy. That is
/// an ordinary transient failure; let it propagate untyped.
/// </para>
/// </remarks>
public sealed class InboxDependencyUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InboxDependencyUnavailableException"/> class.
    /// </summary>
    public InboxDependencyUnavailableException()
        : base("An inbox dependency is unavailable.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxDependencyUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public InboxDependencyUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxDependencyUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The underlying connectivity fault.</param>
    public InboxDependencyUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
