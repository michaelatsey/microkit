namespace MicroKit.Messaging.Dispatch;

/// <summary>
/// Source-generated log methods for the transport dispatch path.
/// </summary>
/// <remarks>
/// Separate from <c>OutboxProcessorLogs</c> because the processor's events describe the batch
/// engine — claims, settlements, retry curves — while this one describes what left the process.
/// An operator correlating a missing message with a broker's own logs reads these.
/// </remarks>
internal static partial class TransportDispatcherLogs
{
    /// <remarks>
    /// <b>Debug, not Information.</b> One event per dispatched message at the module's default
    /// batch size of 100 would dominate an application's logs on a busy queue, and the useful
    /// signal — throughput, and whether anything is leaving at all — is a rate rather than a
    /// sequence of lines. The processor already logs the batch summary at a coarser level.
    /// </remarks>
    [LoggerMessage(EventId = 1300, Level = LogLevel.Debug,
        Message = "Sent message {MessageId} to the transport as contract '{ContractName}' " +
                  "from source '{Source}'.")]
    public static partial void EnvelopeSent(
        ILogger logger, Guid messageId, string contractName, string source);
}
