namespace MicroKit.Messaging.Publishing;

/// <summary>Source-generated log methods for <see cref="IntegrationEventPublisher"/>.</summary>
/// <remarks>
/// <c>[LoggerMessage]</c> emits a cached delegate per message, so a log call allocates nothing and
/// boxes no argument, and the generator inserts the <c>IsEnabled</c> guard.
/// </remarks>
internal static partial class IntegrationEventLogs
{
    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Debug,
        Message = "Staged integration event {MessageId} as '{ContractName}' from '{Source}' " +
                  "for tenant {TenantId}.")]
    public static partial void Staged(
        ILogger logger, Guid messageId, string contractName, string source, string? tenantId);

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Debug,
        Message = "Integration event {MessageId} ('{ContractName}') was staged without an " +
                  "occurrence time. The transport will fall back to the staging time, which is " +
                  "one relay later than the fact.")]
    public static partial void OccurrenceTimeNotSupplied(
        ILogger logger, Guid messageId, string contractName);
}
