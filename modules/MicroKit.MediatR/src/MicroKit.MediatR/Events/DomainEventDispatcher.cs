using MicroKit.Domain.Events;

namespace MicroKit.MediatR.Events;

/// <summary>
/// The single implementation of <see cref="IDomainEventsDispatcher"/>. Drains the domain events
/// accumulated on tracked aggregates, dispatches every one of them to its registered
/// <see cref="IDomainEventHandler{TEvent}"/> implementations, and then hands the whole batch to
/// every registered <see cref="IDomainEventsSink"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a <b>scoped</b> service. Called by <c>TransactionBehavior</c>
/// (in <c>MicroKit.MediatR.Behaviors</c>) after the command handler completes, before
/// <c>IUnitOfWork.CommitAsync</c> — so sinks stage their work inside the same transaction.
/// </para>
/// <para>
/// <strong>Composition by contribution (ADR-MEDIATR-014).</strong> This type owns the sequence;
/// what it does not own it delegates to an ordered, possibly empty collection of
/// <see cref="IDomainEventsSink"/> resolved from DI. MicroKit.MediatR registers zero sinks;
/// installing <c>MicroKit.Messaging.MediatR</c> contributes the outbox sink. Because Microsoft DI
/// resolves <c>IEnumerable&lt;T&gt;</c> to every registration for <c>T</c>, the composition is
/// order-independent by construction.
/// </para>
/// <para>
/// Command handlers must <b>not</b> call this interface directly. Domain events should be
/// accumulated on aggregates via <see cref="IDomainEventsProvider"/>; the dispatcher is invoked by
/// the pipeline behavior.
/// </para>
/// </remarks>
internal sealed class DomainEventDispatcher : IDomainEventsDispatcher
{
    private readonly IDomainEventsProvider _eventsProvider;
    private readonly IDomainEventHandlerDispatcher _handlerDispatcher;
    private readonly IDomainEventsSink[] _sinks;
    private readonly DomainEventNotificationCatalog _notifications;

    // Precomputed once per scope (ADR-MEDIATR-015). What it costs depends on which of the two
    // supported no-sink configurations this is:
    //   • no sink AND the scan found no notification type — false, and a batch pays exactly one
    //     bool read: no factory call, no allocation, no per-event work at all.
    //   • no sink BUT notifications are declared (routed by something other than a sink, which
    //     ADR-MEDIATR-015 keeps supported) — true, and every batch walks its events, paying one
    //     GetType() and one dictionary lookup each. Allocation-free, but not free.
    private readonly bool _mustGuardNotifications;

    // An explicit constructor rather than a primary one: _mustGuardNotifications is derived from
    // _sinks, and one instance field initializer cannot reference another.
    public DomainEventDispatcher(
        IDomainEventsProvider eventsProvider,
        IDomainEventHandlerDispatcher handlerDispatcher,
        IEnumerable<IDomainEventsSink> sinks,
        DomainEventNotificationCatalog notifications)
    {
        _eventsProvider = eventsProvider;
        _handlerDispatcher = handlerDispatcher;
        // Microsoft DI materializes IEnumerable<T> as an array; the fallback covers other containers.
        _sinks = sinks as IDomainEventsSink[] ?? [.. sinks];
        _notifications = notifications;
        _mustGuardNotifications = _sinks.Length == 0 && !notifications.IsEmpty;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// A drained event maps to a notification but no <see cref="IDomainEventsSink"/> is registered,
    /// so that notification would be silently discarded (ADR-MEDIATR-015).
    /// </exception>
    public async Task DispatchEventsAsync(CancellationToken ct = default)
    {
        // P1 — drain. One pass, not recursive: events raised by a P2 handler are not dispatched
        // in this pass. Pre-existing and deliberate (ADR-MEDIATR-014).
        var domainEvents = _eventsProvider.DrainDomainEvents();
        if (domainEvents.Count == 0) return;

        // Zero sinks is a supported configuration — handlers only, no notifications. It stops being
        // supported the moment an event that maps to a notification is drained, because that
        // notification has nowhere to go and would vanish without a trace.
        //
        // BEFORE P2, not after: this dispatch is already doomed, and a P2 handler is under no
        // obligation to be undoable by a rollback — the canonical example in this module's own docs
        // sends a welcome email. Failing here costs exactly what failing later did (the same bool,
        // the same walk over the same batch) and fails before the first side effect.
        if (_mustGuardNotifications) ThrowIfAnyEventMaps(domainEvents);

        // P2 — synchronous, in-transaction dispatch to IDomainEventHandler<TEvent>.
        //
        // THE BARRIER: this loop completes for EVERY event before any sink runs. Do not fuse it
        // with the sink loop below — the guarantee that a P2 handler can never observe partial
        // sink output is the reason the seam sits exactly here (ADR-MEDIATR-014 Rationale 5).
        foreach (var domainEvent in domainEvents)
            await _handlerDispatcher.DispatchAsync(domainEvent, ct).ConfigureAwait(false);

        // Sinks run in registration order, in-transaction, fail-fast: a throwing sink aborts the
        // command exactly as a throwing handler does. Every sink receives the SAME list instance —
        // no per-sink copy, which is what lets the contract forbid mutating it.
        foreach (var sink in _sinks)
            await sink.ReceiveAsync(domainEvents, ct).ConfigureAwait(false);
    }

    private void ThrowIfAnyEventMaps(IReadOnlyList<IDomainEvent> domainEvents)
    {
        foreach (var domainEvent in domainEvents)
        {
            var eventType = domainEvent.GetType();
            if (!_notifications.TryGetNotificationType(eventType, out var notificationType))
                continue;

            // FullName, not Name: the last remedy below tells the developer to remove notification
            // subclasses "from the scanned assemblies", and a short name identifies neither the
            // assembly nor the namespace. In a modular monolith the same event name legitimately
            // exists in two bounded contexts. Both types are concrete and closed here, so FullName
            // is never null.
            throw new InvalidOperationException(
                $"Domain event '{eventType.FullName}' maps to notification " +
                $"'{notificationType.FullName}', " +
                $"but no {nameof(IDomainEventsSink)} is registered. Notifications are created and " +
                $"staged by a sink; with none registered every notification is silently discarded " +
                $"— handlers still run, nothing is ever published. Register a sink: call " +
                $"AddMediatRDomainEvents() on your MessagingBuilder (MicroKit.Messaging.MediatR), " +
                $"or contribute your own with " +
                $"TryAddEnumerable(ServiceDescriptor.Scoped<{nameof(IDomainEventsSink)}, YourSink>()). " +
                $"If this application uses IDomainEventHandler<TEvent> only, remove the " +
                $"DomainEventNotification<TEvent> subclasses from the scanned assemblies.");
        }
    }
}
