using System.Runtime.CompilerServices;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// The tenants in back-off on one shared resource — an outbox endpoint of a store, or a store's
/// inbox (#124) — with the half-open probe that tells a tenant's failure from the resource's.
/// </summary>
/// <remarks>
/// <para>
/// <b>The unit of a down verdict is the tenant.</b> A failure that takes a resource down for one
/// tenant — a tenant's credentials revoked, its dependency de-provisioned — puts that tenant in
/// back-off, on the curve of the resource's own back-off (<see cref="BackoffCurve"/>), and every other
/// tenant's work goes on. A success of the tenant clears its entry.
/// </para>
/// <para>
/// <b>The probe</b> (the half-open state of a circuit breaker). Once a tenant is blocked, the next
/// down verdict from a tenant that has no entry is the probe failing: the cause is shared, so that
/// tenant is blocked too and <see cref="OutageScope.Shared"/> tells the caller to back the whole
/// resource off — the endpoint, the store's inbox retries, the transport's deliveries. A success in
/// between clears the probe. A tenant whose own back-off just ended and fails again re-blocks only
/// itself, unless the resource is <i>suspected</i> — it backed off and no success has cleared it
/// since — where it counts as a probe like any other: an outage costs at most two attempts per batch,
/// and two tenants that never recover cannot hold the resource for a third.
/// </para>
/// <para>
/// <b>Bounded, and free when idle.</b> An entry leaves on its tenant's success, or once it has been
/// expired for a whole ceiling (<see cref="Prune"/>), past which the curve starts over anyway. With no
/// tenant in back-off, every check is one volatile read (<see cref="IsEmpty"/>, <see cref="IsQuiet"/>):
/// no lock, no lookup, no allocation. Thread-safe: a store's inbox state is shared by its retry loop
/// and the transports' delivery threads.
/// </para>
/// </remarks>
internal sealed class TenantBackoff
{
    private readonly TimeSpan _initial;
    private readonly TimeSpan _ceiling;
    private readonly StrongBox<int>? _loud;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
    private int _count;
    private bool _quiet = true;
    private bool _probePending;
    private bool _suspected;

    /// <summary>Initializes a new <see cref="TenantBackoff"/>.</summary>
    /// <param name="initial">The base delay.</param>
    /// <param name="ceiling">The largest delay.</param>
    /// <param name="loud">
    /// Counts the instances of a group that are not quiet, so the group answers "nothing to do" with one
    /// read whatever its size — the inbox's, one instance per store. Null for a standalone instance.
    /// </param>
    public TenantBackoff(TimeSpan initial, TimeSpan ceiling, StrongBox<int>? loud = null)
    {
        _initial = initial;
        _ceiling = ceiling;
        _loud = loud;
    }

    /// <summary>The scope of a down verdict.</summary>
    internal enum OutageScope
    {
        /// <summary>The tenant's alone: only its work waits.</summary>
        Tenant,

        /// <summary>The probe failed: the cause is shared, and the whole resource backs off.</summary>
        Shared,
    }

    /// <summary>Gets a value indicating whether no tenant is in back-off.</summary>
    public bool IsEmpty => Volatile.Read(ref _count) == 0;

    /// <summary>
    /// Gets a value indicating whether a success has nothing to clear: no tenant in back-off, no probe
    /// pending, the resource not suspected.
    /// </summary>
    public bool IsQuiet => Volatile.Read(ref _quiet);

    /// <summary>Whether <paramref name="tenant"/> is in back-off at <paramref name="now"/>.</summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="now">The current time.</param>
    /// <param name="until">When its back-off ends, when it is in force.</param>
    /// <returns><see langword="true"/> while its back-off is in force.</returns>
    public bool IsBlocked(string tenant, DateTimeOffset now, out DateTimeOffset until)
    {
        until = default;
        if (IsEmpty)
        {
            return false;
        }

        lock (_lock)
        {
            if (!_slots.TryGetValue(tenant, out var slot))
            {
                return false;
            }

            until = slot.Until;
            return now < slot.Until;
        }
    }

    /// <summary>The tenants in back-off at <paramref name="now"/>, for the claim to leave out (#124 A7).</summary>
    /// <param name="now">The current time.</param>
    /// <returns>
    /// The tenants whose back-off is in force; the shared empty array when there is none, so the happy
    /// path allocates nothing. One array per call otherwise: once per batch, per endpoint for the outbox.
    /// </returns>
    public string[] BlockedAt(DateTimeOffset now)
    {
        if (IsEmpty)
        {
            return [];
        }

        lock (_lock)
        {
            var blocked = 0;
            foreach (var slot in _slots.Values)
            {
                if (now < slot.Until)
                {
                    blocked++;
                }
            }

            if (blocked == 0)
            {
                return [];
            }

            var tenants = new string[blocked];
            var i = 0;
            foreach (var (tenant, slot) in _slots)
            {
                if (now < slot.Until)
                {
                    tenants[i++] = tenant;
                }
            }

            return tenants;
        }
    }

    /// <summary>
    /// The current delay of <paramref name="tenant"/>'s entry — it doubles on each failure, so it ranks
    /// the tenants that failed least — whether or not its back-off is still in force.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="delay">Its entry's delay.</param>
    /// <returns><see langword="true"/> when the tenant has an entry.</returns>
    public bool TryGetDelay(string tenant, out TimeSpan delay)
    {
        delay = default;
        if (IsEmpty)
        {
            return false;
        }

        lock (_lock)
        {
            if (!_slots.TryGetValue(tenant, out var slot))
            {
                return false;
            }

            delay = slot.Delay;
            return true;
        }
    }

    /// <summary>
    /// Records a down verdict from <paramref name="tenant"/>: puts it in back-off, and says whether the
    /// cause is its own or shared.
    /// </summary>
    /// <param name="tenant">The tenant whose work met the verdict.</param>
    /// <param name="now">The time of the failure.</param>
    /// <param name="until">When the tenant's back-off ends.</param>
    /// <returns>
    /// <see cref="OutageScope.Shared"/> when this verdict is the probe failing: the caller backs the whole
    /// resource off. The resource is then suspected until a success.
    /// </returns>
    public OutageScope OnDown(string tenant, DateTimeOffset now, out DateTimeOffset until)
    {
        lock (_lock)
        {
            var known = _slots.TryGetValue(tenant, out var previous);
            var delay = BackoffCurve.Next(known ? previous.Until : null, previous.Delay, now, _initial, _ceiling);
            var slot = new Slot(now + delay, delay);
            _slots[tenant] = slot;
            until = slot.Until;

            OutageScope scope;
            if (known && !_suspected)
            {
                // Its own back-off ended and it failed again: its own cause, nobody else's.
                scope = OutageScope.Tenant;
            }
            else if (_probePending)
            {
                // The probe failed: a second tenant down before any success. The failed probe's tenant
                // is blocked too, or two tenants that never recover would trip the resource for ever.
                _probePending = false;
                _suspected = true;
                scope = OutageScope.Shared;
            }
            else
            {
                _probePending = true;
                scope = OutageScope.Tenant;
            }

            Refresh();
            return scope;
        }
    }

    /// <summary>
    /// Marks the resource suspected: it backed off for every tenant — a failed probe, a null tenant's
    /// verdict, the store itself unreachable — and no success has cleared it yet.
    /// </summary>
    public void MarkSuspected()
    {
        lock (_lock)
        {
            _probePending = false;
            _suspected = true;
            Refresh();
        }
    }

    /// <summary>
    /// Records a success on the resource: clears <paramref name="tenant"/>'s entry, the probe, and the
    /// suspicion. One volatile read when there is nothing to clear.
    /// </summary>
    /// <param name="tenant">The tenant of the work that succeeded; <see langword="null"/> for none.</param>
    public void Succeeded(string? tenant)
    {
        if (IsQuiet)
        {
            return;
        }

        lock (_lock)
        {
            if (tenant is not null)
            {
                _slots.Remove(tenant);
            }

            _probePending = false;
            _suspected = false;
            Refresh();
        }
    }

    /// <summary>Removes the entries expired for a whole ceiling: the curve starts over past that.</summary>
    /// <param name="now">The current time.</param>
    public void Prune(DateTimeOffset now)
    {
        if (IsEmpty)
        {
            return;
        }

        lock (_lock)
        {
            foreach (var (tenant, slot) in _slots)
            {
                if (now >= slot.Until + _ceiling)
                {
                    _slots.Remove(tenant);
                }
            }

            Refresh();
        }
    }

    /// <summary>Publishes the counters the lock-free reads test. Called under the lock.</summary>
    private void Refresh()
    {
        var count = _slots.Count;
        if (count == 0)
        {
            // Nobody left to have failed first: a later failure is a first one again.
            _probePending = false;
        }

        Volatile.Write(ref _count, count);

        var quiet = count == 0 && !_probePending && !_suspected;
        if (quiet == _quiet)
        {
            return;
        }

        Volatile.Write(ref _quiet, quiet);
        if (_loud is not null)
        {
            if (quiet)
            {
                Interlocked.Decrement(ref _loud.Value);
            }
            else
            {
                Interlocked.Increment(ref _loud.Value);
            }
        }
    }

    private readonly record struct Slot(DateTimeOffset Until, TimeSpan Delay);
}
