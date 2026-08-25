namespace MicroKit.Messaging;

/// <summary>
/// Marks a type as an integration event: a fact one bounded context publishes for others.
/// </summary>
/// <remarks>
/// <para>
/// <b>A marker, deliberately (ADR-MSG-018).</b> This interface used to declare
/// <c>MessageId</c>, <c>TenantId</c>, <c>CorrelationId</c>, <c>CausationId</c> and
/// <c>OccurredOnUtc</c>. Every one of them then existed twice — on the event instance and on the
/// persisted message row — with nothing keeping the two in agreement. An event built in a test, a
/// tenant set before the ambient context resolved, and the row was written with one tenant while
/// the trace said another: a discrepancy nothing detects, on the field that governs isolation.
/// </para>
/// <para>
/// The identity was already contradictory. The interface required a <c>MessageId</c>, and the
/// only thing that ever deduplicated on it was the inbox — which keys on the <i>outbox row's</i>
/// id, not the event's. The declared identity was a field the contract demanded and the system
/// worked around.
/// </para>
/// <para>
/// An integration event now carries business payload only. Everything about its delivery lives on
/// the message row, assigned at staging by <see cref="IIntegrationEventPublisher"/> from the
/// ambient execution context:
/// </para>
/// <code>
/// [IntegrationEvent("saasbtp.safety.constat-recorded.v1")]
/// public sealed record ConstatRecorded(Guid ConstatId, Guid SiteId) : IIntegrationEvent;
/// </code>
/// <para>
/// Distinct from a domain event, which never leaves its aggregate's transaction, and from a
/// domain event notification, which never leaves the process. Three types rather than one is what
/// lets an architecture test forbid an aggregate from referencing this one.
/// </para>
/// <para>
/// <b>The <see cref="IEvent"/> base stays.</b> It ties this interface to <c>MicroKit.Domain</c>,
/// the canonical event-taxonomy root, which would matter if the contract had to travel outward —
/// but it does not. A transport carries an envelope: a contract name, a source, a serialized
/// payload, metadata. Serialization happens at publication, inside this module, so nothing
/// downstream ever names this type. The coupling exists and stops at the edge of one assembly.
/// </para>
/// <para>
/// It does NOT extend <c>IDomainEvent</c>, <c>INotification</c>, or any MediatR type.
/// <c>Abstractions</c>, <c>Core</c>, <c>EntityFrameworkCore</c> and the broker providers have zero
/// MediatR dependency, enforced by architecture tests; the single exception is the
/// <c>MicroKit.Messaging.MediatR</c> glue package (ADR-MSG-009 carve-out).
/// </para>
/// </remarks>
public interface IIntegrationEvent : IEvent;
