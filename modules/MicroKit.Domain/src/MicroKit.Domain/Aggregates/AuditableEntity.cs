using System.Globalization;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects.Common;

namespace MicroKit.Domain.Aggregates;

/// <summary>
/// Base class for auditable domain entities with identity-based equality and audit tracking.
/// Extends the standard entity behavior with lightweight audit metadata.
/// </summary>
/// <typeparam name="TId">The strongly-typed identifier type</typeparam>
/// <remarks>
/// The creation fields are set once, by the constructor, from an explicit instant or a
/// <see cref="TimeProvider"/> and an <see cref="Actor"/>. The update fields change only through
/// <c>Touch</c>, which the derived entity calls from each state-mutating method. No audit value
/// is read from an ambient clock or an ambient security context.
/// </remarks>
public abstract class AuditableEntity<TId> : Entity<TId>, IAuditableEntity
    where TId : IEntityId
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuditableEntity{TId}"/> class.
    /// </summary>
    /// <param name="id">The strongly-typed identifier for this entity.</param>
    /// <param name="createdAt">
    /// The instant this entity was created. Stored at offset zero; any offset is accepted.
    /// </param>
    /// <param name="createdBy">
    /// The actor creating this entity. Use <see cref="Actor.System"/> when the action has
    /// no user in context, rather than leaving the creator unattributed.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="createdBy"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="createdAt"/> is <c>default(DateTimeOffset)</c>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Every instant passed to a constructor or to <c>Touch</c> is normalised to offset zero with
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
    /// to a constructor; give the derived entity a private constructor for materialisation that
    /// takes the scalar members only and passes a placeholder such as <see cref="Actor.System"/> to
    /// this constructor, which requires a non-null actor. EF Core then overwrites
    /// <see cref="CreatedBy"/> with the stored value.
    /// </para>
    /// </remarks>
    protected AuditableEntity(TId id, DateTimeOffset createdAt, Actor createdBy)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(createdBy);

        CreatedAt = UtcInstant.Normalize(createdAt, nameof(createdAt));
        CreatedBy = createdBy;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditableEntity{TId}"/> class,
    /// reading the creation instant from the supplied <see cref="TimeProvider"/>.
    /// </summary>
    /// <param name="id">The strongly-typed identifier for this entity.</param>
    /// <param name="timeProvider">
    /// The time source. Pass <c>TimeProvider.System</c> in production, or a fake provider
    /// in tests. This is an injected dependency, never an ambient static clock.
    /// </param>
    /// <param name="createdBy">The actor creating this entity.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="timeProvider"/> or <paramref name="createdBy"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Use this constructor from the factory that creates a new entity. EF Core cannot bind it on
    /// materialisation: <see cref="TimeProvider"/> is neither a mapped property nor a service EF
    /// Core injects.
    /// </remarks>
    protected AuditableEntity(TId id, TimeProvider timeProvider, Actor createdBy)
        : this(
            id,
            (timeProvider ?? throw new ArgumentNullException(nameof(timeProvider))).GetUtcNow(),
            createdBy)
    {
    }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private init; }

    /// <inheritdoc />
    public Actor CreatedBy { get; private init; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Actor? UpdatedBy { get; private set; }

    /// <summary>
    /// Records that this entity was modified, updating <see cref="UpdatedAt"/> and
    /// <see cref="UpdatedBy"/> together.
    /// </summary>
    /// <param name="updatedAt">
    /// The instant of the modification. Stored at offset zero; any offset is accepted.
    /// </param>
    /// <param name="updatedBy">The actor performing the modification.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="updatedBy"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="updatedAt"/> is <c>default(DateTimeOffset)</c>, or precedes the
    /// current <see cref="UpdatedAt"/>, or <see cref="CreatedAt"/> when there was no update yet. An
    /// instant equal to it is accepted.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Call this at the end of every state-mutating method. Routing both fields through a
    /// single member is what prevents them from drifting apart: with independent setters it
    /// is possible to update the timestamp and forget the actor, producing an audit trail
    /// that records when something changed but not who changed it.
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
    /// </remarks>
    protected void Touch(DateTimeOffset updatedAt, Actor updatedBy)
    {
        ArgumentNullException.ThrowIfNull(updatedBy);

        var instant = UtcInstant.Normalize(updatedAt, nameof(updatedAt));
        var bound = UpdatedAt ?? CreatedAt;
        if (instant < bound)
            throw new ArgumentOutOfRangeException(
                nameof(updatedAt), updatedAt,
                string.Create(CultureInfo.InvariantCulture,
                    $"A modification at {instant:O} cannot precede {bound:O}, the {(UpdatedAt is null ? "creation instant" : "previous modification")}."));

        UpdatedAt = instant;
        UpdatedBy = updatedBy;
    }
}
