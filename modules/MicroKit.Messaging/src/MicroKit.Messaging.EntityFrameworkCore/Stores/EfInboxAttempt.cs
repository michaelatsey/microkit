namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>
/// Whether this scope's guarded inbox attempt has begun and not committed (#188): scoped, shared by
/// the scope's <see cref="EfInboxStore{TContext}"/>, which marks it, and its
/// <see cref="EfOutboxStore{TContext}"/>, which reads it at the end of the scope.
/// </summary>
/// <remarks>
/// A row still <c>Added</c> when the scope ends is lost (4000) unless this scope's attempt is rolling
/// back with it (4001). Any other open transaction — a host's own, attached through
/// <c>UseTransaction</c> and committed by its owner after the scope — proves nothing about the row.
/// </remarks>
/// <typeparam name="TContext">The store's context.</typeparam>
internal sealed class EfInboxAttempt<TContext>
    where TContext : DbContext
{
    /// <summary>Gets whether an attempt has begun and has not committed.</summary>
    public bool Uncommitted { get; private set; }

    /// <summary>The attempt's transaction is open.</summary>
    public void Begun() => Uncommitted = true;

    /// <summary>The attempt's transaction committed.</summary>
    public void Committed() => Uncommitted = false;
}
