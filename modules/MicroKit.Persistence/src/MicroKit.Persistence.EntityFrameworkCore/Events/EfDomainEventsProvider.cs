namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Change-tracker-backed <see cref="IDomainEventsProvider"/> that aggregates domain events
/// across <b>every</b> entity currently tracked by <typeparamref name="TContext"/>.
/// </summary>
/// <typeparam name="TContext">
/// The application <see cref="DbContext"/> type. Injected as a scoped service.
/// </typeparam>
/// <remarks>
/// <para>
/// <b>Two kinds of implementer exist at different scopes — this is the distinction that makes
/// <see cref="IDomainEventsProvider"/> easy to misread.</b>
/// </para>
/// <list type="table">
///   <item>
///     <term>Aggregate scope</term>
///     <description>
///     <c>AggregateRoot&lt;TId&gt;</c> drains its <b>own</b> events. It is never registered in
///     the container — aggregates are loaded, not resolved.
///     </description>
///   </item>
///   <item>
///     <term>Unit-of-work scope</term>
///     <description>
///     This type drains events from <b>all</b> aggregates tracked by
///     <typeparamref name="TContext"/> in the current scope. It is the implementation
///     registered in DI, and the one that satisfies the drain phase of domain-event dispatch.
///     </description>
///   </item>
/// </list>
/// <para>
/// Resolving <see cref="IDomainEventsProvider"/> from the container therefore yields the
/// unit-of-work-scoped provider, never an individual aggregate. Supplying an aggregate as this
/// contract would drain one arbitrary instance and silently miss every other tracked aggregate.
/// </para>
/// <para>
/// Registered as scoped by
/// <see cref="PersistenceServiceCollectionExtensions.AddUnitOfWork{TContext}"/>.
/// This type never queries the database: it reads only the in-memory change tracker.
/// </para>
/// <para>
/// Enumerating the change tracker triggers <c>DetectChanges</c> when
/// <see cref="ChangeTracker.AutoDetectChangesEnabled"/> is on (the default) — once per call,
/// not once per entity. This is deliberately not suppressed: the drain runs before
/// <c>CommitAsync</c>, and suppressing detection here would change the caller's
/// change-tracking semantics for the commit that follows. Callers who disable
/// auto-detection are responsible for their own <c>DetectChanges</c> call — otherwise
/// aggregates reachable only via an untracked navigation will not be drained.
/// </para>
/// </remarks>
public sealed class EfDomainEventsProvider<TContext>(TContext context) : IDomainEventsProvider
    where TContext : DbContext
{
    /// <summary>
    /// Gets a snapshot of every domain event pending on all tracked entities that expose them
    /// via <see cref="IHasDomainEvents"/>. Reading does not clear — repeated reads return the
    /// same events until <see cref="DrainDomainEvents"/> is called.
    /// </summary>
    /// <remarks>
    /// Candidates are detected on <see cref="IHasDomainEvents"/>, the read contract, so this
    /// member also reports events from entities that expose them but cannot be drained. Those
    /// entities are skipped by <see cref="DrainDomainEvents"/>; the two members can therefore
    /// legitimately report different sets.
    /// </remarks>
    public IReadOnlyList<IDomainEvent> DomainEvents
    {
        get
        {
            List<IDomainEvent>? events = null;

            foreach (var entry in context.ChangeTracker.Entries<IHasDomainEvents>())
            {
                var pending = entry.Entity.DomainEvents;
                if (pending.Count == 0)
                    continue;

                (events ??= []).AddRange(pending);
            }

            return events is null ? Array.Empty<IDomainEvent>() : events.AsReadOnly();
        }
    }

    /// <summary>
    /// Drains every domain event pending on all tracked aggregates, clearing each aggregate's
    /// collection as it goes.
    /// </summary>
    /// <returns>
    /// All drained events, in change-tracker enumeration order. An empty collection if no
    /// tracked aggregate had pending events.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Draining requires <see cref="IDomainEventsProvider"/>. A tracked entity that implements
    /// only <see cref="IHasDomainEvents"/> exposes its events but offers no way to take them,
    /// and is skipped: it has opted out of the drain contract by construction and is not an
    /// aggregate participating in dispatch.
    /// </para>
    /// <para>
    /// Each aggregate's drain is atomic in itself. Nothing in this loop can fail, so no
    /// aggregate is ever left partially consumed.
    /// </para>
    /// </remarks>
    public IReadOnlyList<IDomainEvent> DrainDomainEvents()
    {
        List<IDomainEvent>? events = null;

        foreach (var entry in context.ChangeTracker.Entries<IHasDomainEvents>())
        {
            if (entry.Entity is not IDomainEventsProvider provider)
                continue;

            var drained = provider.DrainDomainEvents();
            if (drained.Count == 0)
                continue;

            (events ??= []).AddRange(drained);
        }

        return events is null ? Array.Empty<IDomainEvent>() : events.AsReadOnly();
    }
}
