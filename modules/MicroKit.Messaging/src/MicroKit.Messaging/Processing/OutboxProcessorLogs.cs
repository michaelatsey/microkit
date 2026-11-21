namespace MicroKit.Messaging.Processing;

/// <summary>
/// Source-generated log methods for <see cref="OutboxProcessor"/> and
/// <see cref="OutboxRetentionLoop"/>.
/// </summary>
/// <remarks>
/// <c>[LoggerMessage]</c> emits a cached delegate per message, so a log call allocates
/// nothing and boxes no argument, and the generator inserts the <c>IsEnabled</c> guard —
/// the manual guard the previous implementation wrote by hand is no longer needed.
/// </remarks>
internal static partial class OutboxProcessorLogs
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Debug,
        Message = "Outbox batch claimed {ClaimedCount} message(s).")]
    public static partial void BatchClaimed(ILogger logger, int claimedCount);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Outbox message {MessageId} ({EventType}) failed transiently " +
                  "(attempt {Attempt}/{MaxRetries}). Next attempt at {NextRetryAtUtc:O}.")]
    public static partial void TransientFailure(
        ILogger logger,
        Exception exception,
        Guid messageId,
        string eventType,
        int attempt,
        int maxRetries,
        DateTimeOffset nextRetryAtUtc);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Outbox message {MessageId} ({EventType}) exhausted its retry budget " +
                  "({MaxRetries}). Dead-lettering.")]
    public static partial void RetriesExhausted(
        ILogger logger, Exception exception, Guid messageId, string eventType, int maxRetries);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "Outbox message {MessageId} ({EventType}) is permanently undeliverable. " +
                  "Dead-lettering without retry.")]
    public static partial void PermanentFailure(
        ILogger logger, Exception exception, Guid messageId, string eventType);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Error,
        Message = "Outbox endpoint {Endpoint} is unavailable (null: the transport); the accompanying " +
                  "exception says why. Event 1010 says what is deferred once the batch is fully settled.")]
    public static partial void TransportUnavailable(ILogger logger, Exception exception, string? endpoint);

    /// <remarks>
    /// Deferral has its own event (ADR-MSG-019's constraint, kept by ADR-MSG-020): 1004 and 1009 say
    /// why an endpoint is down, this one what waits for it.
    /// </remarks>
    [LoggerMessage(
        EventId = 1010,
        Level = LogLevel.Warning,
        Message = "Outbox endpoint {Endpoint} (null: the transport) is down: {DeferredCount} row(s) of " +
                  "the batch were settled to wait, the first until {From:O}, the last until {Until:O}, " +
                  "no retry consumed.")]
    public static partial void EndpointDeferred(
        ILogger logger, string? endpoint, int deferredCount, DateTimeOffset from, DateTimeOffset until);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Information,
        Message = "Outbox batch cancelled. Releasing {ReleasedCount} unattempted message(s).")]
    public static partial void BatchCancelled(ILogger logger, int releasedCount);

    /// <remarks>
    /// Deliberately names no particular registration. <c>OutboxConfigurationException</c> reaches
    /// this event from two kinds of place that a single sentence cannot cover: something the
    /// processor resolves from the scope is not registered at all — the dispatcher of a row's endpoint,
    /// in a container composed outside the store engine; or a dispatcher was handed a row it
    /// structurally cannot serve. The exception carries which one, and
    /// is logged with this event; a headline naming only the first sends an operator to check a
    /// registration that is already there. A dispatcher that is registered but cannot be
    /// <i>activated</i> — a transport dispatcher with no <c>IMessageTransport</c> — does not come
    /// here: that is <see cref="DispatcherActivationFailed"/>, and the worker keeps running.
    /// </remarks>
    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Critical,
        Message = "The outbox cannot dispatch — a required registration is missing; the " +
                  "accompanying exception says which. Abandoning batch and releasing " +
                  "{ReleasedCount} message(s) with no retry consumed. Retrying will not help: " +
                  "fix the composition root and redeploy.")]
    public static partial void DispatchMisconfigured(
        ILogger logger, Exception exception, int releasedCount);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Warning,
        Message = "Outbox settlement wrote {WrittenCount} of {OutcomeCount} outcome(s). " +
                  "The missing leases expired mid-batch and are now owned elsewhere.")]
    public static partial void PartialSettlement(ILogger logger, int writtenCount, int outcomeCount);

    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Error,
        Message = "Failed to settle outbox batch of {OutcomeCount} message(s). " +
                  "Leases will expire naturally; dispatched messages may be redelivered.")]
    public static partial void SettlementFailed(ILogger logger, Exception exception, int outcomeCount);

    /// <remarks>
    /// <para>
    /// An endpoint deferred with no rethrow and no retry movement, so this event is the only trace it
    /// leaves and must never be silent. It repeats each time the endpoint's back-off ends, until the
    /// cause clears.
    /// </para>
    /// <para>
    /// <b>It names the row whose dispatcher could not be built</b> — id, tenant and kind. Activation
    /// runs in that row's own scope, so a tenant-scoped cause fails for some rows only, and one that
    /// never clears — a de-provisioned tenant — fails it again each time the endpoint's back-off ends,
    /// holding that endpoint's rows of every tenant. These three values are how an operator finds it.
    /// #124 is the known defect.
    /// </para>
    /// <para>
    /// <c>Error</c>, not <c>Critical</c>, matching <see cref="TransportUnavailable"/> — the other arm
    /// that defers an endpoint without stopping the worker. <c>Critical</c> belongs to
    /// <see cref="DispatchMisconfigured"/>, which does stop it. The commonest cause is a transport
    /// dispatcher registered with no <c>IMessageTransport</c>; the accompanying exception is the
    /// container's own <see cref="InvalidOperationException"/> — the only type this event reports —
    /// and names the type it could not supply.
    /// </para>
    /// </remarks>
    [LoggerMessage(
        EventId = 1009,
        Level = LogLevel.Error,
        Message = "The outbox dispatcher is registered but could not be activated for message " +
                  "{MessageId} (tenant {TenantId}, endpoint {Endpoint}); the accompanying exception " +
                  "says why. Event 1010 says what is deferred once the batch is fully settled.")]
    public static partial void DispatcherActivationFailed(
        ILogger logger, Exception exception, Guid messageId, string? tenantId, string? endpoint);

    [LoggerMessage(
        EventId = 1020,
        Level = LogLevel.Debug,
        Message = "Outbox retention deleted {DeletedCount} published message(s) processed before {Cutoff:O}.")]
    public static partial void RetentionPass(ILogger logger, int deletedCount, DateTimeOffset cutoff);

    [LoggerMessage(
        EventId = 1021,
        Level = LogLevel.Information,
        Message = "Outbox retention is disabled (RetentionDays = {RetentionDays}). " +
                  "Published rows will accumulate until a retention policy is configured.")]
    public static partial void RetentionDisabled(ILogger logger, int retentionDays);

    [LoggerMessage(
        EventId = 1022,
        Level = LogLevel.Error,
        Message = "Outbox retention pass failed. Retrying after {Interval}.")]
    public static partial void RetentionFailed(ILogger logger, Exception exception, TimeSpan interval);
}
