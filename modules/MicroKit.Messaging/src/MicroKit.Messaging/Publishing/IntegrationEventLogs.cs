namespace MicroKit.Messaging.Publishing;

/// <summary>Source-generated log methods for <see cref="IntegrationEventPublisher"/>.</summary>
/// <remarks>
/// <c>[LoggerMessage]</c> emits a cached delegate per message, so a log call allocates nothing and
/// boxes no argument, and the generator inserts the <c>IsEnabled</c> guard.
/// </remarks>
internal static partial class IntegrationEventLogs
{
    /// <summary>
    /// Records a staged contract row, <b>origin included</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><paramref name="originMessageId"/> is the only way to tell deduplication on from
    /// deduplication off, after the fact.</b> It is half of the replay key on
    /// (<c>OriginMessageId</c>, <c>ContractName</c>), and a null one does not deduplicate, because
    /// nulls are distinct in that unique index. Publishing outside a dispatch — a command handler,
    /// a job — legitimately produces a null and is the common case. Publishing from a scope that is
    /// not the dispatch scope produces the same null and is a defect. Without this field the two
    /// are indistinguishable in every log a running system emits, which is the one silent state
    /// this whole mechanism is built to avoid.
    /// </para>
    /// <para>
    /// Almost every wrong-scope publish is caught earlier and loudly: the staging writer is scoped
    /// over the same <c>DbContext</c> as the caller's unit of work, so a publish from another scope
    /// finds no open transaction and <c>IIntegrationEventPublisher</c> refuses it. The case that
    /// survives that guard is a handler that opens a transaction inside a scope of its own — and
    /// this field is what makes it visible.
    /// </para>
    /// </remarks>
    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Debug,
        Message = "Staged integration event {MessageId} in the outbox as '{ContractName}' from " +
                  "'{Source}' for tenant {TenantId}, origin {OriginMessageId} (empty when " +
                  "published outside a dispatch, which does not deduplicate).")]
    public static partial void Staged(
        ILogger logger,
        Guid messageId,
        string contractName,
        string source,
        string? tenantId,
        Guid? originMessageId);

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Debug,
        Message = "Integration event {MessageId} ('{ContractName}') was staged without an " +
                  "occurrence time, so the staging time was recorded instead — one relay later " +
                  "than the fact. This log line is the only remaining trace of the difference.")]
    public static partial void OccurrenceTimeNotSupplied(
        ILogger logger, Guid messageId, string contractName);

    /// <summary>Records an absorbed replay.</summary>
    /// <remarks>
    /// <paramref name="originMessageId"/> is <see cref="Nullable{T}"/> so this call cannot depend on
    /// an invariant that belongs to a different package. The shipped
    /// <c>EfIntegrationEventWriter</c> rethrows rather than verifying when the origin is null — a
    /// null origin cannot collide on the replay key — so in practice this is always populated. But
    /// <c>IIntegrationEventWriter</c> is a port, and a second implementation returning
    /// <c>AlreadyPublishedAs</c> for a null-origin row would otherwise throw
    /// <see cref="NullReferenceException"/> from inside a diagnostic call: a fault manufactured by
    /// the logging of a success.
    /// </remarks>
    [LoggerMessage(
        EventId = 1202,
        Level = LogLevel.Debug,
        Message = "Integration event '{ContractName}' was already published from origin " +
                  "{OriginMessageId}; returning the existing message {MessageId}. This is the " +
                  "nominal outcome of a redelivered dispatch, not a fault.")]
    public static partial void AlreadyPublished(
        ILogger logger, Guid messageId, string contractName, Guid? originMessageId);
}
