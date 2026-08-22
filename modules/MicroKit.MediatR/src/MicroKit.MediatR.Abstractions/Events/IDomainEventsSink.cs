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
/// orchestrator resolves every registered sink as <c>IEnumerable&lt;IDomainEventsSink&gt;</c>, so the
/// composition is independent of the order in which registration methods were called
/// (ADR-MEDIATR-014). MicroKit.MediatR itself registers no sinks.
/// </para>
/// <para>
/// <strong>Register with <c>TryAddEnumerable</c>.</strong> It deduplicates on
/// <c>(ServiceType, ImplementationType)</c>; a sink registered twice receives every batch twice and
/// therefore writes twice.
/// </para>
/// <code>
/// services.TryAddEnumerable(ServiceDescriptor.Scoped&lt;IDomainEventsSink, MySink&gt;());
/// </code>
/// <para>
/// <strong>The descriptor must carry an implementation type.</strong> Registration by
/// implementation type, by instance, or with the <em>two-type-argument</em> factory overload
/// (<c>ServiceDescriptor.Scoped&lt;IDomainEventsSink, MySink&gt;(sp =&gt; new MySink(arg))</c> — use this
/// when the sink takes constructor arguments DI cannot supply) all work. A descriptor whose
/// implementation type cannot be recovered — the one-type-argument
/// <c>ServiceDescriptor.Scoped&lt;IDomainEventsSink&gt;(sp =&gt; …)</c>, or the non-generic
/// <c>Describe</c> overload — is rejected with <see cref="ArgumentException"/>
/// ("indistinguishable from other services"), because <c>TryAddEnumerable</c> then has nothing to
/// deduplicate on.
/// </para>
/// <para>
/// <strong>Lifetime: register scoped</strong> unless the sink has no scoped dependencies. A sink is
/// resolved from — and stages into — the same scope as the unit of work the dispatch runs in, so a
/// singleton sink holding a scoped collaborator is a captive dependency.
/// </para>
/// <para>
/// <strong>Sinks run in-transaction, in registration order, fail-fast.</strong> On the command path
/// a sink is invoked inside the caller's unit of work, before the commit; a sink that throws aborts
/// the command. It stages work in that unit of work — it is <b>not</b> a place for I/O to an
/// external system.
/// </para>
/// <para>
/// <strong>Two invocation paths — only one of them commits.</strong> The orchestrator is called from
/// the command pipeline by <c>TransactionBehavior</c> (inside the transaction, before
/// <c>IUnitOfWork.CommitAsync</c>), and again on the outbox-processing path after the
/// <c>INotificationHandler</c> executions for a dispatched message — cascade dispatch, contributed
/// by <c>MicroKit.Messaging.MediatR</c>. Only the command path is followed by a commit:
/// <c>TransactionBehavior</c> is the sole owner of <c>SaveChanges</c> and is not on the cascade
/// path, so work a sink stages there is currently <b>never flushed</b> and is dropped without a
/// trace (ADR-MEDIATR-015 § Consequences; <c>L0-FINDINGS.md</c> Finding #3, open). Do not assume
/// every invocation is followed by a commit.
/// </para>
/// </remarks>
public interface IDomainEventsSink
{
    /// <summary>
    /// Receives one drained batch. Never called with an empty batch.
    /// </summary>
    /// <param name="domainEvents">
    /// The events drained for this pass, in drain order. Every registered
    /// <see cref="IDomainEventHandler{TEvent}"/> has already completed for every event here.
    /// <para>
    /// The same instance is passed to every sink in the batch. Do not mutate it — casting it back
    /// to a mutable form and changing it alters what later sinks see. Do not retain it beyond the
    /// call either; it is not guaranteed to stay valid once dispatch returns.
    /// </para>
    /// </param>
    /// <param name="ct">Propagates notification that operations should be cancelled.</param>
    ValueTask ReceiveAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken ct = default);
}
