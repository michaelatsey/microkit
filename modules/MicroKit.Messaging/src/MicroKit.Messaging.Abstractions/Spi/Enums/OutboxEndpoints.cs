namespace MicroKit.Messaging.Spi;

/// <summary>
/// The endpoints of a store's outbox, as flags: the transport (a row whose
/// <see cref="OutboxMessage.Endpoint"/> is <see langword="null"/>) and the local endpoint
/// (<see cref="OutboxMessage.LocalEndpoint"/>).
/// </summary>
/// <remarks>
/// Part of the SPI (<c>MicroKit.Messaging.Spi</c>). The processor passes the endpoints in back-off to
/// <see cref="IOutboxProcessorStore.ClaimBatchAsync"/>,
/// so a down endpoint's rows are not claimed while it is down and the other endpoint's rows are
/// (#188).
/// </remarks>
[Flags]
public enum OutboxEndpoints
{
    /// <summary>No endpoint.</summary>
    None = 0,

    /// <summary>The transport: rows with no <see cref="OutboxMessage.Endpoint"/>.</summary>
    Transport = 1,

    /// <summary>The local endpoint: rows routed <see cref="OutboxMessage.LocalEndpoint"/>.</summary>
    Local = 2,
}
