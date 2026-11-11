namespace MicroKit.Messaging.Bus;

/// <summary>Source-generated log messages of the bus and the in-memory transport.</summary>
internal static partial class BusLogs
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Debug,
        Message = "Published message {MessageId} as '{WireType}' from '{Source}' for tenant {TenantId}.")]
    public static partial void Published(
        ILogger logger, string messageId, string wireType, string source, string? tenantId);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Warning,
        Message = "Message {MessageId} ('{WireType}' from '{Source}') resolves to local type " +
                  "'{EventType}', but no handler is registered for it. It is acknowledged and " +
                  "nothing ran.")]
    public static partial void NoHandler(
        ILogger logger, string messageId, string wireType, string source, string eventType);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Warning,
        Message = "Handler '{HandlerType}' failed on message {MessageId} ('{WireType}') with " +
                  "verdict {Verdict}.")]
    public static partial void HandlerFailed(
        ILogger logger,
        Exception exception,
        string handlerType,
        string messageId,
        string wireType,
        DeliveryVerdict verdict);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Information,
        Message = "Message {MessageId} ('{WireType}') was interrupted by shutdown in handler " +
                  "'{HandlerType}' and is left unsettled for redelivery.")]
    public static partial void Unsettled(
        ILogger logger, Exception exception, string messageId, string wireType, string handlerType);

    [LoggerMessage(
        EventId = 3010,
        Level = LogLevel.Error,
        Message = "Message {MessageId} ('{Type}') was dead-lettered after {FailedAttempts} failed " +
                  "attempt(s): {Reason}")]
    public static partial void DeadLettered(
        ILogger logger,
        Exception? exception,
        string? messageId,
        string? type,
        int failedAttempts,
        string? reason);

    [LoggerMessage(
        EventId = 3011,
        Level = LogLevel.Warning,
        Message = "Message {MessageId} ('{Type}') failed attempt {FailedAttempts} of " +
                  "{MaxDeliveryAttempts} and is redelivered.")]
    public static partial void Redelivered(
        ILogger logger, string messageId, string type, int failedAttempts, int maxDeliveryAttempts);

    [LoggerMessage(
        EventId = 3012,
        Level = LogLevel.Warning,
        Message = "A dependency is unavailable while handling message {MessageId} ('{Type}'). The " +
                  "in-memory endpoint pauses for {Backoff}; the message is redelivered without " +
                  "counting an attempt.")]
    public static partial void EndpointPaused(
        ILogger logger, string messageId, string type, TimeSpan backoff);

    [LoggerMessage(
        EventId = 3013,
        Level = LogLevel.Error,
        Message = "An unexpected fault escaped the delivery of message {MessageId} ('{Type}'). The " +
                  "message was {Disposition}.")]
    public static partial void UnexpectedDeliveryFault(
        ILogger logger, Exception exception, string? messageId, string? type, string disposition);

    [LoggerMessage(
        EventId = 3014,
        Level = LogLevel.Warning,
        Message = "Message {MessageId} ('{WireType}' from '{Source}') was sent on the in-memory " +
                  "broker, but no endpoint is bound to its type, so it reaches no one. Start a " +
                  "host whose handlers handle it on the same broker.")]
    public static partial void NoEndpointBound(
        ILogger logger, string messageId, string wireType, string source);

    [LoggerMessage(
        EventId = 3015,
        Level = LogLevel.Debug,
        Message = "In-memory endpoint '{Endpoint}' is bound to {Count} wire type(s): {WireTypes}.")]
    public static partial void EndpointBound(
        ILogger logger, string endpoint, int count, string wireTypes);
}
