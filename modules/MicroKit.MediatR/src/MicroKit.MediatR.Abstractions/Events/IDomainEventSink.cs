using MicroKit.MediatR.Handlers;

namespace MicroKit.MediatR.Events;

/// <summary>
/// Receives the batch of domain events drained for one dispatch pass, after every
/// <see cref="IDomainEventHandler{TEvent}"/> has completed for every event in the batch.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Contribution, not replacement.</strong> A package that extends domain-event dispatch
/// registers a sink; it never registers a rival <c>IDomainEventsDispatcher</c>. The single
/// orchestrator resolves every registered sink as <c>IEnumerable&lt;IDomainEventSink&gt;</c>, so the
/// composition is independent of the order in which registration methods were called
/// (ADR-MEDIATR-014). MicroKit.MediatR itself registers no sinks.
/// </para>
/// <para>
/// <strong>Register with <c>TryAddEnumerable</c>.</strong> It deduplicates on
/// <c>(ServiceType, ImplementationType)</c>; a sink registered twice receives every batch twice and
/// therefore writes twice.
/// </para>
/// <code>
/// services.TryAddEnumerable(ServiceDescriptor.Scoped&lt;IDomainEventSink, MySink&gt;());
/// </code>
/// <para>
/// Register by implementation type (or instance), never by factory lambda:
/// <c>TryAddEnumerable</c> deduplicates on the implementation type, and a factory-based descriptor
/// has none — it throws <see cref="ArgumentException"/> rather than registering.
/// </para>
/// <para>
/// <strong>Sinks run in-transaction, in registration order, fail-fast.</strong> A sink is invoked
/// inside the caller's unit of work, before the commit; a sink that throws aborts the command.
/// It stages work in that unit of work — it is <b>not</b> a place for I/O to an external system.
/// </para>
/// </remarks>
public interface IDomainEventSink
{
    /// <summary>
    /// Receives one drained batch. Never called with an empty batch.
    /// </summary>
    /// <param name="domainEvents">
    /// The events drained for this pass, in drain order. Every registered
    /// <see cref="IDomainEventHandler{TEvent}"/> has already completed for every event here.
    /// </param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    ValueTask ReceiveAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken ct = default);
}
