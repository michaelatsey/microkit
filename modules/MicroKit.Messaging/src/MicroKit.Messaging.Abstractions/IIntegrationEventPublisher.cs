namespace MicroKit.Messaging;

/// <summary>The only way application code emits an integration event.</summary>
/// <remarks>
/// <para>
/// <b>This stages a row. It does not commit, and it does not deliver.</b> The caller's unit of
/// work commits. If that transaction rolls back, the event was never published — which is the
/// entire point: nothing is announced for a fact that did not happen. Delivery comes later, from a
/// relay draining the queue.
/// </para>
/// <para>
/// Exactly the contract <see cref="IOutboxWriter.AddAsync"/> has one transaction earlier, and the
/// symmetry is the design: every relay in this pipeline stages into the transaction of whatever
/// produced the fact, and none owns a commit of its own.
/// </para>
/// <para>
/// <b>The caller never sees the transport.</b> No broker, no serializer, no consumer list, no
/// tenant plumbing. It states that something happened.
/// </para>
///
/// <para>
/// <b>The caller MUST be inside a unit of work it opened, and that is easy to get wrong.</b>
/// Staging is only meaningful inside an open transaction: without one the row goes to a change
/// tracker nobody saves, and the event silently never existed. This method therefore throws
/// <see cref="IntegrationEventPublishException"/> rather than accept a publication it cannot
/// honour.
/// </para>
/// <para>
/// The trap is that committing is not the same as opening a transaction.
/// <c>IUnitOfWork.CommitAsync</c> is a bare <c>SaveChangesAsync</c>: it runs under the provider's
/// implicit per-call transaction, which never appears as an open transaction on the context, so a
/// handler that only calls it is rejected. Opening one explicitly is what
/// <c>ITransactionalContext.ExecuteAsync</c> does, so publishing belongs inside that callback:
/// </para>
/// <code>
/// public sealed class ConstatRecordedHandler(
///     IIntegrationEventPublisher publisher,
///     ITransactionalContext transaction,
///     IUnitOfWork unitOfWork)
/// {
///     public Task HandleAsync(ConstatRecordedEvent domainEvent, CancellationToken ct) =&gt;
///         transaction.ExecuteAsync(
///             static async (state, token) =&gt;
///             {
///                 await state.Publisher.PublishAsync(
///                     new ConstatRecorded(state.ConstatId, state.SiteId),
///                     occurredOnUtc: state.OccurredAt,
///                     token);
///
///                 // Everything the handler staged — its own rows and the integration event —
///                 // commits here, in one transaction.
///                 await state.UnitOfWork.CommitAsync(token);
///             },
///             (Publisher: publisher,
///              UnitOfWork: unitOfWork,
///              domainEvent.ConstatId,
///              domainEvent.SiteId,
///              domainEvent.OccurredAt),
///             ct);
/// }
/// </code>
/// <para>
/// <b>Expected call site: a domain event notification handler.</b> That is where a domain fact
/// becomes a published contract. MicroKit enforces only the mechanical requirement — an open
/// transaction — because a consuming application without this notification model may legitimately
/// publish from elsewhere. The flow rule itself belongs to the application, as an architecture
/// test.
/// </para>
/// </remarks>
public interface IIntegrationEventPublisher
{
    /// <summary>Stages one integration event for publication.</summary>
    /// <typeparam name="TEvent">
    /// The event type. Must be registered and carry <see cref="IntegrationEventAttribute"/>.
    /// </typeparam>
    /// <param name="integrationEvent">The event. Business payload only.</param>
    /// <param name="occurredOnUtc">
    /// When the underlying fact happened, if known. A notification handler has this on the domain
    /// event and should pass it: without it the only timestamp on the row is the staging time,
    /// which in this flow is one relay later than the fact — minutes under load, hours after an
    /// incident. Omitted, the transport falls back to the staging time, which is an honest
    /// approximation rather than a silent one.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// The identifier assigned to the staged message: the one value linking this business
    /// operation to what a consumer eventually receives, and what its inbox deduplicates on.
    /// </returns>
    /// <exception cref="IntegrationEventConfigurationException">
    /// The event type is not registered.
    /// </exception>
    /// <exception cref="IntegrationEventPublishException">
    /// The caller has no open transaction. See the remarks on this interface.
    /// </exception>
    /// <remarks>
    /// There is no batch overload, deliberately. Staging performs no I/O, so a loop over this
    /// method costs nothing a batch would save — while an API shaped like a batch suggests an
    /// atomicity or an optimisation that does not exist. A small API is harder to misuse; one can
    /// be added under the rule of three.
    /// </remarks>
    ValueTask<MessageId> PublishAsync<TEvent>(
        TEvent integrationEvent,
        DateTimeOffset? occurredOnUtc = null,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent;
}
