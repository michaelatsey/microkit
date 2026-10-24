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
/// <c>Mutate</c>, which wraps each state-mutating method. No audit value
/// is read from an ambient clock or an ambient security context.
/// </remarks>
public abstract class AuditableAggregateRoot<TId> : AggregateRoot<TId>, IAuditableEntity
    where TId : IEntityId
{
    // Explicit backing fields: AuditRules.Mutate writes them by reference. EF Core finds them by
    // the _camelCase convention and materialises through them, as it did the auto-property fields.
    private DateTimeOffset? _updatedAt;
    private Actor? _updatedBy;

    // The number of modifications recorded, so AuditRules.Mutate can tell a nested one apart from an
    // earlier one carrying the same stamp. Not part of the audit trail; not mapped.
    private int _modifications;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditableAggregateRoot{TId}"/> class.
    /// </summary>
    /// <param name="id">The strongly-typed identifier for this aggregate root.</param>
    /// <param name="createdAt">
    /// The instant this aggregate was created. Stored at offset zero; any offset is accepted.
    /// </param>
    /// <param name="createdBy">
    /// The actor creating this aggregate. Use <see cref="Actor.System"/> when the action
    /// has no user in context.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="createdBy"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="createdAt"/> is <c>default(DateTimeOffset)</c>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Every instant passed to a constructor or to <c>Mutate</c> is normalised to offset zero with
    /// <see cref="DateTimeOffset.ToUniversalTime"/>: the instant is preserved, only its
    /// representation changes, so <c>DateTimeOffset.Now</c> is accepted. <c>default(DateTimeOffset)</c>
    /// is rejected. A value EF Core materialises through the backing field bypasses the rule and is
    /// kept as the database returned it.
    /// </para>
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
        CreatedAt = AuditRules.Creation(createdAt, createdBy);
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
        : this(id, AuditRules.Now(timeProvider), createdBy)
    {
    }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private init; }

    /// <inheritdoc />
    public Actor CreatedBy { get; private init; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get => _updatedAt; private set => _updatedAt = value; }

    /// <inheritdoc />
    public Actor? UpdatedBy { get => _updatedBy; private set => _updatedBy = value; }

    /// <summary>
    /// Applies a state change to this aggregate and records it, updating <see cref="UpdatedAt"/> and
    /// <see cref="UpdatedBy"/> together once the change has succeeded.
    /// </summary>
    /// <param name="at">
    /// The instant of the modification. Stored at offset zero; any offset is accepted.
    /// </param>
    /// <param name="by">The actor performing the modification.</param>
    /// <param name="mutation">The state change, run once the instant and the actor are validated.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="by"/> or <paramref name="mutation"/> is <see langword="null"/>.
    /// Nothing has changed.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="at"/> is <c>default(DateTimeOffset)</c>, or precedes the current
    /// <see cref="UpdatedAt"/>, or <see cref="CreatedAt"/> when there was no update yet. An instant
    /// equal to it is accepted. Nothing has changed: the mutation has not run.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Auditing wraps the mutation instead of following it. The instant and the actor are validated
    /// first, before anything changes; then <paramref name="mutation"/> runs; only if it returns are
    /// <see cref="UpdatedAt"/> and <see cref="UpdatedBy"/> set. If it throws, the audit fields are
    /// restored to the values they had before it ran, even if a nested <c>Mutate</c> on this object
    /// succeeded inside it, and the exception propagates.
    /// </para>
    /// <para>
    /// Domain events raised inside a <paramref name="mutation"/> that throws are removed, so a
    /// failed operation publishes no fact.
    /// </para>
    /// <para>
    /// The limit: a mutation that throws halfway keeps the field changes it made before throwing,
    /// on this object and on any other: a child entity whose own <c>Mutate</c> succeeded inside it
    /// keeps its audit stamp. Only this object's audit fields and events are left as they were.
    /// Check the business rules (<c>CheckRule</c>) at the start of the mutation, before its first
    /// assignment and before mutating any child, so that a broken rule throws while nothing has
    /// changed. After a mutation throws, the aggregate instance must be discarded, not saved: it may
    /// hold the partial changes (ADR-004 D3).
    /// </para>
    /// <para>
    /// A nested <c>Mutate</c> on the same object, run inside <paramref name="mutation"/>, may record a
    /// stamp at or after this call's instant. That stamp is kept: this call does not overwrite it with
    /// an earlier instant, nor credit an equal one to its own actor. A nested stamp earlier than this
    /// call's instant is superseded.
    /// </para>
    /// <para>
    /// <see cref="UpdatedAt"/> never decreases. An instant earlier than the current one is rejected
    /// rather than clamped: clamping would record the actor against an instant that is not when the
    /// change happened, and would hide a skewed clock instead of surfacing it. The accepted cost: when
    /// a host's nodes' clocks disagree by more than its own update cadence, the skew surfaces as this
    /// exception rather than as a silent drift. A node whose clock is behind gets it at its own write;
    /// a node whose clock is ahead writes successfully, and the healthy nodes then get it until real
    /// time passes the instant it recorded.
    /// </para>
    /// <para>
    /// A <paramref name="mutation"/> lambda that captures a parameter or a local, as the example
    /// does, allocates a closure and a delegate on each call. That cost is negligible next to the
    /// I/O that persists the change, so there is a single overload (ADR-004 D2).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public void Rename(string name, DateTimeOffset now, Actor actor) =>
    ///     Mutate(now, actor, () =>
    ///     {
    ///         CheckRule(new NameMustNotBeEmptyRule(name)); // rules first, before any assignment
    ///         Name = name;
    ///     });
    /// </code>
    /// </example>
    protected void Mutate(DateTimeOffset at, Actor by, Action mutation) =>
        AuditRules.Mutate(at, by, mutation, CreatedAt, ref _updatedAt, ref _updatedBy, ref _modifications, journal: this);
}
