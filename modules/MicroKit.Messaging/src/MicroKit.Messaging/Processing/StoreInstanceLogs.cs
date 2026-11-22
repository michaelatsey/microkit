namespace MicroKit.Messaging.Processing;

/// <summary>
/// Logs of the instances of partitioned stores (#191): their lifecycle, their source, and their loops.
/// Every event carries the store's key in <c>{Store}</c> and the instance's id in <c>{Instance}</c>, so
/// a query by store key keeps working; a single store's loops keep events 1400–1403.
/// </summary>
internal static partial class StoreInstanceLogs
{
    [LoggerMessage(
        EventId = 1404,
        Level = LogLevel.Information,
        Message = "Draining instance '{Instance}' of store '{Store}'.")]
    public static partial void InstanceStarted(ILogger logger, string store, string instance);

    [LoggerMessage(
        EventId = 1405,
        Level = LogLevel.Information,
        Message = "Instance '{Instance}' of store '{Store}' was removed by its source: each of its loops " +
                  "settles the batch it holds, then stops claiming. Its rows stay where they are.")]
    public static partial void InstanceRemoved(ILogger logger, string store, string instance);

    [LoggerMessage(
        EventId = 1406,
        Level = LogLevel.Debug,
        Message = "The loops of instance '{Instance}' of store '{Store}' have ended: its back-off state and " +
                  "its metric series are released.")]
    public static partial void InstanceReleased(ILogger logger, string store, string instance);

    [LoggerMessage(
        EventId = 1407,
        Level = LogLevel.Error,
        Message = "The instance source of store '{Store}' failed. The instances already drained keep " +
                  "draining, and the source is read again on its next change.")]
    public static partial void SourceFailed(ILogger logger, Exception exception, string store);

    [LoggerMessage(
        EventId = 1408,
        Level = LogLevel.Error,
        Message = "The instance source of store '{Store}' returned instance '{Instance}' twice. The snapshot " +
                  "is ignored: the instances already drained keep draining, and the source is read again " +
                  "on its next change.")]
    public static partial void DuplicateInstance(ILogger logger, string store, string instance);

    [LoggerMessage(
        EventId = 1409,
        Level = LogLevel.Critical,
        Message = "Store '{Store}' failed its check at the start of instance '{Instance}': {Problems} No " +
                  "instance of the store is drained, and its deliveries are redelivered and counted, until " +
                  "a fix is deployed and the host restarted. Other stores are drained.")]
    public static partial void StoreRefused(ILogger logger, string store, string instance, string problems);

    [LoggerMessage(
        EventId = 1410,
        Level = LogLevel.Warning,
        Message = "Handler '{HandlerType}' of message {MessageId} (tenant {TenantId}) is guarded by store " +
                  "'{Store}', and the delivery maps to no instance this host drains. A counted failure: " +
                  "the transport redelivers it, and its other messages are not held.")]
    public static partial void DeliveryNotDrained(
        ILogger logger, string handlerType, Guid messageId, string? tenantId, string store);

    [LoggerMessage(
        EventId = 1411,
        Level = LogLevel.Error,
        Message = "The {Loop} loop of instance '{Instance}' of store '{Store}' could not activate {Service}: " +
                  "it is registered, and its construction threw. Retrying after {Delay}. Nothing was claimed.")]
    public static partial void ActivationFailed(
        ILogger logger, Exception exception, string loop, string store, string instance, string service, TimeSpan delay);

    [LoggerMessage(
        EventId = 1412,
        Level = LogLevel.Error,
        Message = "The {Loop} loop of instance '{Instance}' of store '{Store}' failed; retrying after {Delay}. " +
                  "No other instance is affected.")]
    public static partial void LoopFailed(
        ILogger logger, Exception exception, string loop, string store, string instance, TimeSpan delay);

    [LoggerMessage(
        EventId = 1413,
        Level = LogLevel.Critical,
        Message = "The {Loop} loop of instance '{Instance}' of store '{Store}' met a configuration fault; " +
                  "every claimed row was released, and it retries after {Delay}. It cannot clear without a " +
                  "fix: correct the composition and redeploy.")]
    public static partial void ConfigurationFault(
        ILogger logger, Exception exception, string loop, string store, string instance, TimeSpan delay);

    [LoggerMessage(
        EventId = 1415,
        Level = LogLevel.Error,
        Message = "Instance '{Instance}' of store '{Store}' could not be started; it is tried again on the " +
                  "source's next change. The other instances are unaffected.")]
    public static partial void InstanceStartFailed(ILogger logger, Exception exception, string store, string instance);

    [LoggerMessage(
        EventId = 1414,
        Level = LogLevel.Error,
        Message = "The {Loop} loop of instance '{Instance}' of store '{Store}' could not prepare its scope: " +
                  "the instance initializer, or the store's check, threw. Retrying after {Delay}. Nothing " +
                  "was claimed, and no other instance waits for it.")]
    public static partial void PreparationFailed(
        ILogger logger, Exception exception, string loop, string store, string instance, TimeSpan delay);
}
