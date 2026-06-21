namespace MicroKit.Messaging.Outbox;

/// <summary>
/// Stateless factory for <see cref="OutboxMessage"/>. Singleton-safe: <see cref="IExecutionContext"/>
/// is passed as a method parameter (ADR-MSG-008) to prevent captive-dependency issues when
/// the factory is registered as a singleton.
/// </summary>
public sealed class OutboxMessageFactory(IMessageSerializer serializer)
{
    /// <summary>
    /// Creates an <see cref="OutboxMessage"/> from an arbitrary payload, intrinsic event identifiers,
    /// and the ambient execution context.
    /// </summary>
    /// <param name="payload">
    /// The object to serialize into <see cref="OutboxMessage.Payload"/>. Any runtime type is accepted;
    /// serialization uses <c>payload.GetType()</c>. In the domain-event flow this is typically an
    /// <c>IDomainEventNotification{TEvent}</c> (from <c>MicroKit.Messaging.MediatR</c>).
    /// </param>
    /// <param name="messageId">
    /// The stable, end-to-end identifier. Supplied by the caller from the domain event's intrinsic
    /// <c>EventId</c> so that the outbox row and the inbox dedup key share the same identity.
    /// </param>
    /// <param name="occurredOnUtc">
    /// The UTC time at which the domain event occurred. Sourced from the domain event's intrinsic
    /// <c>OccurredAt</c> property.
    /// </param>
    /// <param name="context">
    /// The ambient execution context supplying <c>TenantId</c>, <c>CorrelationId</c>, and
    /// <c>CausationId</c>. Passed as a method parameter (not constructor-injected) because this
    /// factory is a singleton.
    /// </param>
    /// <returns>A new <see cref="OutboxMessage"/> with <see cref="OutboxMessageStatus.Pending"/> status.</returns>
    public OutboxMessage Create(
        object payload,
        Guid messageId,
        DateTimeOffset occurredOnUtc,
        IExecutionContext context)
    {
        CorrelationId correlationId =
            Guid.TryParse(context.CorrelationId, out var cGuid)
                ? CorrelationId.From(cGuid)
                : CorrelationId.New();

        CausationId? causationId =
            Guid.TryParse(context.CausationId, out var caGuid)
                ? CausationId.From(caGuid)
                : null;

        return new OutboxMessage
        {
            Id = MessageId.From(messageId),
            EventType = payload.GetType().AssemblyQualifiedName!,
            Payload = serializer.Serialize(payload),
            TenantId = context.TenantId,   // null pass-through — ADR-MSG-008 §5
            CorrelationId = correlationId,
            CausationId = causationId,
            OccurredOnUtc = occurredOnUtc,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Status = OutboxMessageStatus.Pending,
            RetryCount = 0,
        };
    }
}
