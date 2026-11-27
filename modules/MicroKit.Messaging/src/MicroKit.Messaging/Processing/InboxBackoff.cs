using System.Runtime.CompilerServices;

namespace MicroKit.Messaging.Processing;

/// <summary>
/// The inbox back-off of one store, or of one instance of a partitioned store (#191): the (consumer,
/// tenant) pairs in back-off, the consumers in consumer-wide back-off, and the two probes that tell a
/// pair's failure from a consumer's and from the store's (#210).
/// </summary>
/// <remarks>
/// <para>
/// <b>The unit is the pair.</b> A down verdict — a handler's <see cref="InboxDependencyUnavailableException"/> on
/// a row or a delivery with a tenant — puts its (consumer, tenant) pair in back-off, on the inbox curve
/// (<see cref="BackoffCurve"/>), and every other pair's work goes on. The consumer is the row's
/// <c>ConsumerType</c>, so a handler class consuming two message types has one back-off. A success of the
/// consumer for the tenant clears that pair only: another consumer's success for the same tenant leaves it.
/// </para>
/// <para>
/// <b>The store probe decides the store-wide back-off</b>, on its evidence since the last reset — any success,
/// a <see cref="OutageScope.Shared"/> verdict, <see cref="MarkSuspected"/>, no entry left (#210 A25). The
/// counted tenants: a down verdict counts its tenant when that tenant had no entry for any consumer before it,
/// or while the store is <i>suspected</i> — one per verdict, as #124's probe counts verdicts. The failed
/// consumers: every consumer with a down verdict since the reset, with the tenants it failed for. Two tenants
/// counted are the probe failing (<see cref="OutageScope.Shared"/>): the caller backs the whole store off — the
/// transport pauses, the retry batch is abandoned — the store is suspected until a success, every consumer's
/// pending probe clears, and no consumer back-off is set. Unless the only failed consumer is C, and C has a
/// consumer-wide back-off on record — in force, or ended and neither pruned nor cleared by C's success: C's own
/// dependency, and C is held again (<see cref="OutageScope.Consumer"/>). A Consumer verdict withdraws C from the
/// evidence: the tenants only C failed for leave the counted tenants, and C leaves the failed consumers. With one
/// consumer nothing is ever on record, and this is #124's probe.
/// </para>
/// <para>
/// <b>The consumer probe never pauses anything.</b> A verdict of consumer C for a tenant that had no entry
/// for C is C's probe event: with C's probe pending from another tenant, C enters consumer-wide back-off
/// (<see cref="OutageScope.Consumer"/>), on the inbox curve, growing from its previous consumer back-off;
/// otherwise C's probe is pending. While C is in it, its work with a tenant waits, and the other consumers
/// go on. A verdict for a tenant that already has an entry for C changes that entry only, so two tenants
/// that never recover hold C at most once: known to C, they cannot hold it again. Only a success of C clears C's probe and C's back-off;
/// C's pending probe also clears once C has no entry left. A store-wide verdict wins: a verdict that is
/// <see cref="OutageScope.Shared"/> sets no consumer back-off, and entering the store-wide back-off
/// (<see cref="OutageScope.Shared"/>, <see cref="MarkSuspected"/>) clears every consumer's pending probe — C's
/// second failure holds it for every tenant only when it is not the store probe failing: another consumer
/// succeeded since, or its tenant already failed for another consumer. So a single later failure of another
/// consumer, for a new tenant, does not pause the store: the withdrawal took C's tenants out of the count. With
/// one consumer none of this occurs, and this state answers exactly as <see cref="TenantBackoff"/> does. Two
/// limits remain. A batch holding only one consumer's rows, before that consumer has any consumer-wide back-off
/// on record, still reads two of its failures, for two tenants with no success between, as the store probe
/// failing, as <see cref="TenantBackoff"/> does: nothing in the verdict tells that from a store-wide outage. And
/// two failures in a row of one consumer on record, with no other consumer failing between, are read as that
/// consumer's own, even in a store-wide outage: X, X then W, W holds X and W each for every tenant and pauses
/// nothing. That is the sequence when each retry claim is filled by one held consumer's backlog in turn, and on
/// the delivery path when one consumer's deliveries fail in a row.
/// </para>
/// <para>
/// <b>Bounded, and free when idle.</b> A pair leaves on its success, a consumer back-off on its consumer's,
/// and either once it has been expired for a whole ceiling (<see cref="Prune"/>), past which the curve starts
/// over anyway. While nothing is in back-off, every check is one volatile read (<see cref="IsEmpty"/>,
/// <see cref="IsQuiet"/>): no lock, no lookup, no allocation. Thread-safe: the state of a store's inbox (or an
/// instance's) is shared by its retry loop and the transports' delivery threads, under one lock.
/// </para>
/// </remarks>
internal sealed class InboxBackoff
{
    private readonly TimeSpan _initial;
    private readonly TimeSpan _ceiling;
    private StrongBox<int>? _loud;
    private readonly Lock _lock = new();

    // tenant → consumer → the pair's back-off.
    private readonly Dictionary<string, Dictionary<string, Slot>> _tenants = new(StringComparer.Ordinal);

    // A consumer's state, while it has a pair, a pending probe or a consumer back-off.
    private readonly Dictionary<string, ConsumerState> _consumers = new(StringComparer.Ordinal);
    private int _pairs;
    private int _consumerBackoffs;
    private int _holds;
    private bool _quiet = true;
    // The store probe's evidence since its last reset — a success, a Shared verdict, MarkSuspected, no entry
    // left (#210 A25): the counted tenants, one per verdict that counted, as the probe of #124 counts verdicts,
    // and every consumer with a down verdict, with the tenants it failed for.
    private readonly List<string> _counted = [];
    private readonly Dictionary<string, HashSet<string>> _failed = new(StringComparer.Ordinal);
    private bool _suspected;

    /// <summary>Initializes a new <see cref="InboxBackoff"/>.</summary>
    /// <param name="initial">The base delay.</param>
    /// <param name="ceiling">The largest delay.</param>
    /// <param name="loud">
    /// Counts the states of a group that are not quiet — one per single store and one per instance of a
    /// partitioned store (#191) — so the group answers "nothing to do" with one read whatever its size. An
    /// instance's state leaves it through <see cref="Detach"/>. Null for a standalone state.
    /// </param>
    public InboxBackoff(TimeSpan initial, TimeSpan ceiling, StrongBox<int>? loud = null)
    {
        _initial = initial;
        _ceiling = ceiling;
        _loud = loud;
    }

    /// <summary>The scope of a down verdict.</summary>
    internal enum OutageScope
    {
        /// <summary>The pair's alone: only that consumer's work for that tenant waits.</summary>
        Tenant,

        /// <summary>
        /// The consumer's probe failed: its work with a tenant waits, for every tenant; the other consumers
        /// go on, and nothing pauses.
        /// </summary>
        Consumer,

        /// <summary>The store probe failed: the cause is shared, and the whole store's inbox backs off.</summary>
        Shared,

        /// <summary>
        /// The pair or its consumer was already in back-off: an attempt begun before — a delivery in flight on
        /// another transport thread. Nothing changes: its work waits to the end of the same back-off, no curve
        /// grows, and no probe is touched.
        /// </summary>
        Held,
    }

    /// <summary>Gets a value indicating whether no pair and no consumer is in back-off.</summary>
    public bool IsEmpty => Volatile.Read(ref _holds) == 0;

    /// <summary>
    /// Gets a value indicating whether a success has nothing to clear: no pair and no consumer in back-off,
    /// no probe pending, the store not suspected.
    /// </summary>
    public bool IsQuiet => Volatile.Read(ref _quiet);

    /// <summary>
    /// Whether <paramref name="consumer"/>'s work for <paramref name="tenant"/> is in back-off at
    /// <paramref name="now"/>: the pair's back-off or the consumer's is in force.
    /// </summary>
    /// <param name="consumer">The consumer key: the row's <c>ConsumerType</c>.</param>
    /// <param name="tenant">The tenant.</param>
    /// <param name="now">The current time.</param>
    /// <param name="until">When the back-off ends — the later of the two in force — when one is.</param>
    /// <returns><see langword="true"/> while a back-off is in force. One volatile read while nothing is in back-off.</returns>
    public bool IsBlocked(string consumer, string tenant, DateTimeOffset now, out DateTimeOffset until)
        => IsBlocked(consumer, tenant, now, out until, out _);

    /// <inheritdoc cref="IsBlocked(string, string, DateTimeOffset, out DateTimeOffset)"/>
    /// <param name="consumer">The consumer key: the row's <c>ConsumerType</c>.</param>
    /// <param name="tenant">The tenant.</param>
    /// <param name="now">The current time.</param>
    /// <param name="until">When the back-off ends — the later of the two in force — when one is.</param>
    /// <param name="pairInForce">
    /// Whether the pair's own back-off is in force: <see langword="false"/> for work held by its consumer's
    /// consumer-wide back-off alone, which event 2013 does not report for its tenant.
    /// </param>
    public bool IsBlocked(string consumer, string tenant, DateTimeOffset now, out DateTimeOffset until, out bool pairInForce)
    {
        until = default;
        pairInForce = false;
        if (IsEmpty)
        {
            return false;
        }

        lock (_lock)
        {
            return InForce(consumer, tenant, now, out until, out pairInForce);
        }
    }

    /// <summary>What the claim leaves out at <paramref name="now"/> (#210 D5).</summary>
    /// <param name="now">The current time.</param>
    /// <returns>
    /// The consumers whose back-off is in force, and the pairs whose back-off is in force, those of an
    /// excluded consumer left out as redundant; <see cref="InboxClaimExclusions.None"/> when nothing is in
    /// force, so a quiet store allocates nothing. Once per batch otherwise.
    /// </returns>
    public InboxClaimExclusions ExclusionsAt(DateTimeOffset now)
    {
        if (IsEmpty)
        {
            return InboxClaimExclusions.None;
        }

        lock (_lock)
        {
            List<string>? consumers = null;
            foreach (var (consumer, state) in _consumers)
            {
                if (state.Backoff is { } backoff && now < backoff.Until)
                {
                    (consumers ??= []).Add(consumer);
                }
            }

            List<(string ConsumerType, string TenantId)>? pairs = null;
            foreach (var (tenant, byConsumer) in _tenants)
            {
                foreach (var (consumer, slot) in byConsumer)
                {
                    if (now < slot.Until && !ConsumerInForce(consumer, now))
                    {
                        (pairs ??= []).Add((consumer, tenant));
                    }
                }
            }

            return consumers is null && pairs is null
                ? InboxClaimExclusions.None
                : new InboxClaimExclusions(consumers?.ToArray() ?? [], pairs?.ToArray() ?? []);
        }
    }

    /// <summary>
    /// The current delay of the pair's entry — it doubles on each failure, so it ranks the pairs that failed
    /// least — whether or not its back-off is still in force.
    /// </summary>
    /// <param name="consumer">The consumer key.</param>
    /// <param name="tenant">The tenant.</param>
    /// <param name="delay">The pair's delay.</param>
    /// <returns><see langword="true"/> when the pair has an entry.</returns>
    public bool TryGetDelay(string consumer, string tenant, out TimeSpan delay)
    {
        delay = default;
        if (IsEmpty)
        {
            return false;
        }

        lock (_lock)
        {
            if (_tenants.TryGetValue(tenant, out var pairs) && pairs.TryGetValue(consumer, out var slot))
            {
                delay = slot.Delay;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Records a down verdict of <paramref name="consumer"/> for <paramref name="tenant"/>: puts the pair in
    /// back-off and says whose the cause is (#210 D2).
    /// </summary>
    /// <param name="consumer">The consumer key: the row's <c>ConsumerType</c>.</param>
    /// <param name="tenant">The tenant of the work that met the verdict.</param>
    /// <param name="now">The time of the failure.</param>
    /// <param name="until">
    /// When the work waits until: the later of the pair's back-off and, when it is in force, the consumer's.
    /// </param>
    /// <returns>
    /// <see cref="OutageScope.Shared"/> when the store probe failed: the caller backs the whole store off, and
    /// the store is suspected until a success. <see cref="OutageScope.Consumer"/> when the consumer's probe
    /// failed: the consumer entered consumer-wide back-off. <see cref="OutageScope.Held"/> when the pair or the
    /// consumer was already in back-off: nothing changed. Otherwise <see cref="OutageScope.Tenant"/>.
    /// </returns>
    public OutageScope OnDown(string consumer, string tenant, DateTimeOffset now, out DateTimeOffset until)
        => OnDown(consumer, tenant, now, out until, out _);

    /// <inheritdoc cref="OnDown(string, string, DateTimeOffset, out DateTimeOffset)"/>
    /// <param name="consumer">The consumer key: the row's <c>ConsumerType</c>.</param>
    /// <param name="tenant">The tenant of the work that met the verdict.</param>
    /// <param name="now">The time of the failure.</param>
    /// <param name="until">When the work waits until.</param>
    /// <param name="pairHeld">
    /// On <see cref="OutageScope.Held"/>, whether the pair's own back-off was in force — rather than only its
    /// consumer's; <see langword="false"/> otherwise.
    /// </param>
    public OutageScope OnDown(string consumer, string tenant, DateTimeOffset now, out DateTimeOffset until, out bool pairHeld)
    {
        lock (_lock)
        {
            // (a) Held: the verdict carries no news — the curves do not grow once per attempt in flight, and
            // neither probe is touched.
            if (InForce(consumer, tenant, now, out until, out pairHeld))
            {
                return OutageScope.Held;
            }

            pairHeld = false;

            // (b) The pair, on the curve from its previous entry.
            var tenantKnown = _tenants.TryGetValue(tenant, out var pairs);
            if (pairs is null)
            {
                pairs = new Dictionary<string, Slot>(StringComparer.Ordinal);
                _tenants[tenant] = pairs;
            }

            var pairKnown = pairs.TryGetValue(consumer, out var previous);
            var delay = BackoffCurve.Next(pairKnown ? previous.Until : null, previous.Delay, now, _initial, _ceiling);
            var slot = new Slot(now + delay, delay);
            pairs[consumer] = slot;
            var state = StateOf(consumer);
            if (!pairKnown)
            {
                _pairs++;
                state.Pairs++;
            }

            until = slot.Until;

            // (c) The store probe's evidence (#210 A25). A tenant counts when it had no entry for any consumer
            // before this verdict, or while the store is suspected; the consumer joins the failed consumers.
            if (!tenantKnown || _suspected)
            {
                _counted.Add(tenant);
            }

            if (!_failed.TryGetValue(consumer, out var failedFor))
            {
                failedFor = new HashSet<string>(StringComparer.Ordinal);
                _failed[consumer] = failedFor;
            }

            failedFor.Add(tenant);

            OutageScope scope;
            if (_counted.Count >= 2)
            {
                if (_failed.Count == 1 && state.Backoff is not null)
                {
                    // Only this consumer failed since the reset, and it was held for every tenant and has not
                    // succeeded since: its own dependency, not the store's. It is held again, on its curve.
                    scope = HoldConsumer(state, now, ref until);
                }
                else
                {
                    // Two tenants counted before any success: the cause is shared. The pairs that failed stay in
                    // back-off too, or two tenants that never recover would trip the store for ever.
                    EnterSuspicion();
                    scope = OutageScope.Shared;
                }
            }
            else if (!pairKnown && state.ProbeTenant is { } probe && !string.Equals(probe, tenant, StringComparison.Ordinal))
            {
                // (d) The consumer probe: a second tenant new to this consumer, and not the store probe failing —
                // its own dependency. It waits for every tenant, growing from its previous consumer back-off.
                scope = HoldConsumer(state, now, ref until);
            }
            else
            {
                if (!pairKnown)
                {
                    state.ProbeTenant = tenant;
                }

                scope = OutageScope.Tenant;
            }

            if (scope is OutageScope.Consumer)
            {
                // The consumer's own dependency explains its failures: it leaves the evidence, and so do the
                // tenants only it failed for.
                Withdraw(consumer);
            }

            Refresh();
            return scope;
        }
    }

    /// <summary>
    /// Marks the store suspected: it backed off for every tenant — a failed probe, a verdict with no tenant, the
    /// store itself unreachable — and no success has cleared it yet. Every consumer's pending probe clears with
    /// the store's: no success has proved the store up since.
    /// </summary>
    public void MarkSuspected()
    {
        lock (_lock)
        {
            EnterSuspicion();
            Refresh();
        }
    }

    /// <summary>
    /// Records a success of <paramref name="consumer"/>: clears its pair for <paramref name="tenant"/>, its probe
    /// and its consumer back-off, and the store probe and the suspicion. Another consumer's pairs, probe and
    /// back-off are left. One volatile read when there is nothing to clear.
    /// </summary>
    /// <param name="consumer">The consumer key of the work that succeeded.</param>
    /// <param name="tenant">Its tenant; <see langword="null"/> for none.</param>
    public void Succeeded(string consumer, string? tenant)
    {
        if (IsQuiet)
        {
            return;
        }

        lock (_lock)
        {
            if (tenant is not null && _tenants.TryGetValue(tenant, out var pairs) && pairs.Remove(consumer))
            {
                _pairs--;
                _consumers[consumer].Pairs--;
                if (pairs.Count == 0)
                {
                    _tenants.Remove(tenant);
                }
            }

            if (_consumers.TryGetValue(consumer, out var state))
            {
                state.ProbeTenant = null;
                if (state.Backoff is not null)
                {
                    state.Backoff = null;
                    _consumerBackoffs--;
                }
            }

            ClearEvidence();
            _suspected = false;
            Refresh();
        }
    }

    /// <summary>
    /// Removes the pairs and the consumer back-offs expired for a whole ceiling: their curves start over past
    /// that.
    /// </summary>
    /// <param name="now">The current time.</param>
    public void Prune(DateTimeOffset now)
    {
        if (IsEmpty)
        {
            return;
        }

        lock (_lock)
        {
            foreach (var (tenant, pairs) in _tenants)
            {
                foreach (var (consumer, slot) in pairs)
                {
                    if (now >= slot.Until + _ceiling)
                    {
                        pairs.Remove(consumer);
                        _pairs--;
                        _consumers[consumer].Pairs--;
                    }
                }

                if (pairs.Count == 0)
                {
                    _tenants.Remove(tenant);
                }
            }

            foreach (var state in _consumers.Values)
            {
                if (state.Backoff is { } backoff && now >= backoff.Until + _ceiling)
                {
                    state.Backoff = null;
                    _consumerBackoffs--;
                }
            }

            Refresh();
        }
    }

    /// <summary>
    /// The number of tenants not recovered at <paramref name="now"/>, for <c>microkit.inbox.tenants.backoff</c>
    /// (#126): a tenant counts once while any of its pairs has not recovered — from its down verdict until a
    /// success clears it, but never once <paramref name="halfOpen"/> has passed since its back-off ended with no
    /// new attempt.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="halfOpen">How long past its end a back-off still counts.</param>
    /// <returns>The tenants not recovered. One volatile read, no lock, while nothing is in back-off.</returns>
    /// <remarks>Allocates nothing: an observable instrument's callback calls it.</remarks>
    public int CountUnrecoveredTenantsAt(DateTimeOffset now, TimeSpan halfOpen)
    {
        if (IsEmpty)
        {
            return 0;
        }

        lock (_lock)
        {
            var unrecovered = 0;
            foreach (var pairs in _tenants.Values)
            {
                foreach (var slot in pairs.Values)
                {
                    if (now < slot.Until + halfOpen)
                    {
                        unrecovered++;
                        break;
                    }
                }
            }

            return unrecovered;
        }
    }

    /// <summary>
    /// The number of consumers whose consumer-wide back-off has not recovered at <paramref name="now"/>, for
    /// <c>microkit.inbox.consumers.backoff</c>, by the same rule as <see cref="CountUnrecoveredTenantsAt"/>.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="halfOpen">How long past its end a back-off still counts.</param>
    /// <returns>The consumers not recovered. One volatile read, no lock, while nothing is in back-off.</returns>
    /// <remarks>Allocates nothing: an observable instrument's callback calls it.</remarks>
    public int CountUnrecoveredConsumersAt(DateTimeOffset now, TimeSpan halfOpen)
    {
        if (IsEmpty)
        {
            return 0;
        }

        lock (_lock)
        {
            var unrecovered = 0;
            foreach (var state in _consumers.Values)
            {
                if (state.Backoff is { } backoff && now < backoff.Until + halfOpen)
                {
                    unrecovered++;
                }
            }

            return unrecovered;
        }
    }

    /// <summary>
    /// Leaves the group this state counted in (#191, an instance released): a loud state stops holding the
    /// group's count above zero, and no later change of this state reaches it.
    /// </summary>
    public void Detach()
    {
        lock (_lock)
        {
            if (_loud is not null && !_quiet)
            {
                Interlocked.Decrement(ref _loud.Value);
            }

            _loud = null;
        }
    }

    /// <summary>The pair's back-off or its consumer's is in force. Called under the lock.</summary>
    private bool InForce(string consumer, string tenant, DateTimeOffset now, out DateTimeOffset until, out bool pairInForce)
    {
        until = default;
        var held = false;
        pairInForce = false;
        if (_tenants.TryGetValue(tenant, out var pairs) && pairs.TryGetValue(consumer, out var pair) && now < pair.Until)
        {
            until = pair.Until;
            held = true;
            pairInForce = true;
        }

        if (_consumers.TryGetValue(consumer, out var state) && state.Backoff is { } backoff && now < backoff.Until)
        {
            if (!held || backoff.Until > until)
            {
                until = backoff.Until;
            }

            held = true;
        }

        return held;
    }

    /// <summary>
    /// Whether <paramref name="consumer"/> has a consumer-wide back-off on record — in force, or ended and not yet
    /// pruned: its rows were held for every tenant, and its backlog may head the claim (#210 D4 as amended).
    /// </summary>
    /// <param name="consumer">The consumer key.</param>
    /// <returns><see langword="true"/> while a consumer-wide back-off of it is on record.</returns>
    public bool WasHeldConsumerWide(string consumer)
    {
        if (IsEmpty)
        {
            return false;
        }

        lock (_lock)
        {
            return _consumers.TryGetValue(consumer, out var state) && state.Backoff is not null;
        }
    }

    /// <summary>Puts the consumer in consumer-wide back-off, on its curve. Called under the lock.</summary>
    private OutageScope HoldConsumer(ConsumerState state, DateTimeOffset now, ref DateTimeOffset until)
    {
        var delay = BackoffCurve.Next(state.Backoff?.Until, state.Backoff?.Delay ?? default, now, _initial, _ceiling);
        if (state.Backoff is null)
        {
            _consumerBackoffs++;
        }

        state.Backoff = new Slot(now + delay, delay);
        state.ProbeTenant = null;
        if (state.Backoff.Value.Until > until)
        {
            until = state.Backoff.Value.Until;
        }

        return OutageScope.Consumer;
    }

    /// <summary>
    /// Takes <paramref name="consumer"/> out of the store probe's evidence, with the tenants only it failed for.
    /// Called under the lock.
    /// </summary>
    private void Withdraw(string consumer)
    {
        if (!_failed.Remove(consumer, out var failedFor))
        {
            return;
        }

        foreach (var tenant in failedFor)
        {
            var another = false;
            foreach (var other in _failed.Values)
            {
                if (other.Contains(tenant))
                {
                    another = true;
                    break;
                }
            }

            if (!another)
            {
                for (var i = _counted.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(_counted[i], tenant, StringComparison.Ordinal))
                    {
                        _counted.RemoveAt(i);
                    }
                }
            }
        }
    }

    /// <summary>Resets the store probe's evidence. Called under the lock.</summary>
    private void ClearEvidence()
    {
        _counted.Clear();
        _failed.Clear();
    }

    /// <summary>The consumer's own back-off is in force. Called under the lock.</summary>
    private bool ConsumerInForce(string consumer, DateTimeOffset now)
        => _consumers.TryGetValue(consumer, out var state) && state.Backoff is { } backoff && now < backoff.Until;

    /// <summary>The consumer's state, created on its first pair. Called under the lock.</summary>
    private ConsumerState StateOf(string consumer)
    {
        if (!_consumers.TryGetValue(consumer, out var state))
        {
            state = new ConsumerState();
            _consumers[consumer] = state;
        }

        return state;
    }

    /// <summary>The store-wide back-off: the store suspected, and every probe cleared. Called under the lock.</summary>
    private void EnterSuspicion()
    {
        ClearEvidence();
        _suspected = true;
        foreach (var state in _consumers.Values)
        {
            state.ProbeTenant = null;
        }
    }

    /// <summary>Publishes the counters the lock-free reads test, and drops what is left empty. Called under the lock.</summary>
    private void Refresh()
    {
        if (_pairs == 0)
        {
            // Nobody left to have failed first: a later failure is a first one again.
            ClearEvidence();
        }

        foreach (var (consumer, state) in _consumers)
        {
            if (state.Pairs == 0)
            {
                // The consumer has nobody left to have failed first either.
                state.ProbeTenant = null;
                if (state.Backoff is null)
                {
                    _consumers.Remove(consumer);
                }
            }
        }

        Volatile.Write(ref _holds, _pairs + _consumerBackoffs);

        var quiet = _pairs == 0 && _consumers.Count == 0 && _counted.Count == 0 && _failed.Count == 0 && !_suspected;
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

    /// <summary>A consumer's probe and consumer-wide back-off, and how many pairs it has.</summary>
    private sealed class ConsumerState
    {
        public int Pairs { get; set; }

        public string? ProbeTenant { get; set; }

        public Slot? Backoff { get; set; }
    }

    private readonly record struct Slot(DateTimeOffset Until, TimeSpan Delay);
}
