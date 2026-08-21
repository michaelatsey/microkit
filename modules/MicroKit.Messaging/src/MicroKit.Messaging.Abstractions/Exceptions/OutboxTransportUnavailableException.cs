namespace MicroKit.Messaging;

/// <summary>
/// Thrown by an <see cref="IOutboxDispatcher"/> when the transport is unreachable,
/// making every remaining message in the batch equally undispatchable.
/// </summary>
/// <remarks>
/// <para>
/// The processor aborts the batch, releases every unattempted message back to
/// <see cref="OutboxMessageStatus.Pending"/>, and consumes no retry budget. That distinction is
/// the whole point: without it, one broker outage fails all N messages, writes N failure rows and
/// burns N retry budgets — then repeats on the next tick until the queue is dead-lettered.
/// </para>
/// <para>
/// <b>Throw for:</b> connection refused, broker shut down, authentication rejected,
/// channel closed — anything where dispatching the next message is certain to fail too.
/// </para>
/// <para>
/// <b>Do not throw for</b> a single message being rejected while the transport is
/// healthy. That is an ordinary transient failure; let it propagate untyped.
/// </para>
/// </remarks>
public sealed class OutboxTransportUnavailableException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="OutboxTransportUnavailableException"/> class.</summary>
    public OutboxTransportUnavailableException()
        : base("The outbox transport is unavailable.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OutboxTransportUnavailableException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    public OutboxTransportUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OutboxTransportUnavailableException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The underlying transport fault.</param>
    public OutboxTransportUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
