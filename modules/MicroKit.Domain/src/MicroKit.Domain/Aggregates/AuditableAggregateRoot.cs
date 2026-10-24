using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects.Common;

namespace MicroKit.Domain.Aggregates;

/// <summary>
/// Base class for auditable aggregate roots in DDD.
/// Combines aggregate root functionality with lightweight audit tracking.
/// </summary>
/// <typeparam name="TId">The strongly-typed identifier type</typeparam>
/// <remarks>
/// The creation fields are set once, by the constructor, from an explicit instant or a
/// <see cref="TimeProvider"/> and an <see cref="Actor"/>. The update fields change only through
/// <c>Touch</c>, which the derived aggregate calls from each state-mutating method. No audit value
/// is read from an ambient clock or an ambient security context.
/// </remarks>
public abstract class AuditableAggregateRoot<TId> : AggregateRoot<TId>, IAuditableEntity
    where TId : IEntityId
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuditableAggregateRoot{TId}"/> class.
    /// </summary>
    /// <param name="id">The strongly-typed identifier for this aggregate root.</param>
    /// <param name="createdAt">The instant this aggregate was created, in UTC.</param>
    /// <param name="createdBy">
    /// The actor creating this aggregate. Use <see cref="Actor.System"/> when the action
    /// has no user in context.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="createdBy"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// EF Core binds constructor parameters to mapped properties by name, so a derived constructor
    /// taking <c>id</c> and <c>createdAt</c> binds <see cref="Entity{TId}.Id"/> and
    /// <see cref="CreatedAt"/> on materialisation.
    /// </para>
    /// <para>
    /// <see cref="CreatedBy"/> binds the same way only when <see cref="Actor"/> is mapped as a scalar
    /// through a value converter, which must read values back with <see cref="Actor.Restore"/>.
    /// Mapped as an owned or complex type, it is not a scalar property and EF Core cannot pass it
    /// to a constructor; give the derived aggregate a private constructor for materialisation that
    /// takes the scalar members only and passes a placeholder such as <see cref="Actor.System"/> to
    /// this constructor, which requires a non-null actor. EF Core then overwrites
    /// <see cref="CreatedBy"/> with the stored value.
    /// </para>
    /// </remarks>
    protected AuditableAggregateRoot(TId id, DateTimeOffset createdAt, Actor createdBy)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(createdBy);

        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditableAggregateRoot{TId}"/> class,
    /// reading the creation instant from the supplied <see cref="TimeProvider"/>.
    /// </summary>
    /// <param name="id">The strongly-typed identifier for this aggregate root.</param>
    /// <param name="timeProvider">
    /// The time source. Pass <c>TimeProvider.System</c> in production, or a fake provider
    /// in tests. Never resolves to an ambient static clock.
    /// </param>
    /// <param name="createdBy">The actor creating this aggregate.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="timeProvider"/> or <paramref name="createdBy"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Use this constructor from the factory that creates a new aggregate. EF Core cannot bind it on
    /// materialisation: <see cref="TimeProvider"/> is neither a mapped property nor a service EF
    /// Core injects.
    /// </remarks>
    protected AuditableAggregateRoot(TId id, TimeProvider timeProvider, Actor createdBy)
        : this(id, (timeProvider ?? throw new ArgumentNullException(nameof(timeProvider)))
            .GetUtcNow(), createdBy)
    {
    }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; protected init; }

    /// <inheritdoc />
    public Actor CreatedBy { get; protected init; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Actor? UpdatedBy { get; private set; }

    /// <summary>
    /// Records that this aggregate was modified, updating <see cref="UpdatedAt"/> and
    /// <see cref="UpdatedBy"/> together.
    /// </summary>
    /// <param name="updatedAt">The instant of the modification, in UTC.</param>
    /// <param name="updatedBy">The actor performing the modification.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="updatedBy"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Call this at the end of every state-mutating method. Routing all updates through a
    /// single member is what prevents the two audit fields from drifting apart — a plain
    /// setter on each one makes it possible to update the timestamp and forget the actor.
    /// </remarks>
    protected void Touch(DateTimeOffset updatedAt, Actor updatedBy)
    {
        ArgumentNullException.ThrowIfNull(updatedBy);

        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }
}
