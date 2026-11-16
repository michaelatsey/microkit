namespace MicroKit.Messaging.Spi;

/// <summary>
/// What a transport must do with a message it handed to <see cref="IEnvelopeReceiver"/>.
/// </summary>
/// <remarks>
/// Part of the SPI (<c>MicroKit.Messaging.Spi</c>): the engine returns it, a transport provider acts
/// on it.
/// </remarks>
public enum ReceiveDisposition
{
    /// <summary>Settled: every handler's outcome is durable; acknowledge the message.</summary>
    Acknowledge,

    /// <summary>Never deliverable by this process: move the message to the dead-letter queue.</summary>
    DeadLetter,

    /// <summary>Not settled: do not acknowledge; redeliver the message under the transport's retry policy.</summary>
    Redeliver,

    /// <summary>A dependency is down: do not acknowledge; pause the endpoint, then redeliver.</summary>
    PauseAndRedeliver,

    /// <summary>The host is stopping: leave the message unsettled; it is never an acknowledgement.</summary>
    Unsettled,
}
