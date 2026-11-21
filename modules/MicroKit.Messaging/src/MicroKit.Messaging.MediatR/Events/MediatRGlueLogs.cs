namespace MicroKit.Messaging.MediatR.Events;

/// <summary>The MediatR glue's own events.</summary>
internal static partial class MediatRGlueLogs
{
    [LoggerMessage(
        EventId = 2310,
        Level = LogLevel.Error,
        Message = "Handler '{HandlerType}' left {Count} domain event(s) undispatched ({EventTypes}) on a host " +
                  "with no store: there is no engine save after a handler, so the bus does not dispatch " +
                  "them — their in-transaction handlers' writes would never be saved. Raise them before a " +
                  "save that dispatches (a MediatR command with the transaction behavior), or configure a " +
                  "store.")]
    public static partial void EventsLeftUndispatched(ILogger logger, string handlerType, int count, string eventTypes);
}
