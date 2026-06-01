namespace MicroKit.Persistence.Abstractions;

/// <summary>
/// Read-side repository marker for <typeparamref name="TAggregate"/> aggregates.
/// Never mutates state. All queries execute without change tracking.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must implement <see cref="IAggregateRoot"/>.
/// </typeparam>
/// <remarks>
/// This interface is intentionally empty in <c>MicroKit.Persistence.Abstractions</c>
/// to preserve the Abstractions minimality rule (ADR-003). The full read contract —
/// <c>ListAsync</c>, <c>ListPagedAsync</c>, <c>AnyAsync</c>, <c>CountAsync</c> —
/// is declared in <c>MicroKit.Persistence</c> (Core) where <c>QueryOptions&lt;TAggregate&gt;</c>
/// is also defined.
/// Inject <see cref="IReadRepository{TAggregate}"/> in query handlers only.
/// The MKP002 analyzer enforces that no mutation methods appear on implementations.
/// </remarks>
public interface IReadRepository<TAggregate>
    where TAggregate : IAggregateRoot;
