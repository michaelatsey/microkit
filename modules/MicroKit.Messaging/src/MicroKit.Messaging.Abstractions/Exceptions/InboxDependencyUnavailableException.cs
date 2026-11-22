namespace MicroKit.Messaging;

/// <summary>
/// Thrown by a handler when a dependency the entire batch relies on is unreachable, making
/// every remaining row equally unprocessable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Application code raises this one.</b> It is part of the application surface: an
/// <see cref="IMessageHandler{T}"/>, a receive middleware or a scope initializer throws it, and the
/// engine catches it and draws a verdict from it. A store raises it too, to report its own database
/// unreachable (<see cref="Spi.IInboxGuardStore"/>); the engine tells the two apart.
/// </para>
/// <para>
/// No attempt is ever counted for it. For a message with a tenant, in a store, it is first that
/// tenant's (#124): the tenant is put in back-off on the store's inbox — a retry's rows wait to the end
/// of it, a new delivery is written to wait the same way and acknowledged — and the store's other
/// tenants are handled meanwhile. Only once a second tenant fails before any success, for a message
/// with no tenant, or without a store, does the retry loop abandon its batch and the transport pause
/// and redeliver. Turning a five-minute outage into a hundred rows pushed one step closer to
/// dead-lettering is the failure mode this prevents.
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
