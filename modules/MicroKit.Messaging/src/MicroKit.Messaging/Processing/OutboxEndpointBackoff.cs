namespace MicroKit.Messaging.Processing;

/// <summary>
/// The back-off of each endpoint of one store's outbox (#188), and of each tenant on it (#124): while
/// an endpoint is down — its transport unreachable, its dispatcher not activatable, a local consumer's
/// dependency out — its rows are deferred, not attempted, and the other endpoint's rows keep flowing;
/// while it is down for one tenant only, only that tenant's rows of it wait.
/// </summary>
/// <remarks>
/// <para>
/// One instance per single store, a singleton keyed by the store — and one per instance of a
/// partitioned store, held by the engine for as long as it drains it (#191) — shared by the scoped
/// <see cref="OutboxProcessor"/> of every cycle: the processor is rebuilt each cycle, the back-off
/// outlives it. The delay starts at the polling interval, doubles on each failed attempt, is capped at
/// <see cref="OutboxProcessorOptions.TransportUnavailableBackoff"/>, and is cleared by the first row
/// the endpoint dispatches; a failure a whole ceiling past the end of the back-off starts again from
/// the base delay (<see cref="BackoffCurve"/>). While it runs, <see cref="BlockedAt"/> tells the claim
/// to leave the endpoint's rows out, so they are neither claimed nor rewritten.
/// </para>
/// <para>
/// A deferred row (<see cref="OutboxOutcomeKind.Deferred"/>) is settled with its retry count and error
/// text unchanged and its next attempt at the end of the back-off, so the claim skips it until then: the endpoint's stuck rows never head the claim, and
/// the endpoint is tried once per back-off period, not once per cycle.
/// </para>
/// <para>
/// <b>Tenants (#124).</b> Each endpoint keeps its tenants in back-off (<see cref="TenantsOf"/>), on the
/// same curve. A down verdict on a row with a tenant blocks that tenant on the endpoint; only the
/// probe failing — a second tenant down before any success — blocks the endpoint as above. While it
/// runs, <see cref="TenantBackoff.BlockedAt"/> tells the claim to leave that tenant's rows of the
/// endpoint out, as for an endpoint; the rows the blocking batch had already claimed are deferred.
/// </para>
/// </remarks>
internal sealed class OutboxEndpointBackoff(TimeSpan initial, TimeSpan ceiling)
{
    private readonly TenantBackoff _transportTenants = new(initial, ceiling);
    private readonly TenantBackoff _localTenants = new(initial, ceiling);
    private Slot? _transport;
    private Slot? _local;

    /// <summary>Whether <paramref name="endpoint"/> is in back-off at <paramref name="now"/>.</summary>
    /// <param name="endpoint">A dispatcher key (<see cref="OutboxDispatchers"/>).</param>
    /// <param name="now">The current time.</param>
    /// <param name="until">When the back-off ends, when it is in force.</param>
    /// <returns><see langword="true"/> while the back-off is in force.</returns>
    public bool IsBlocked(string endpoint, DateTimeOffset now, out DateTimeOffset until)
    {
        var slot = Volatile.Read(ref SlotOf(endpoint));
        until = slot?.Until ?? default;
        return slot is not null && now < slot.Until;
    }

    /// <summary>
    /// Whether <paramref name="endpoint"/> is not recovered at <paramref name="now"/>, for the back-off
    /// up-down counter (#126): from a down verdict until a dispatched row clears it, but never once
    /// <paramref name="halfOpen"/> has passed since its back-off ended with no new attempt.
    /// </summary>
    /// <param name="endpoint">A dispatcher key.</param>
    /// <param name="now">The current time.</param>
    /// <param name="halfOpen">How long past its end a back-off still counts.</param>
    /// <returns><see langword="true"/> while the endpoint is not recovered.</returns>
    public bool IsUnrecoveredAt(string endpoint, DateTimeOffset now, TimeSpan halfOpen)
    {
        var slot = Volatile.Read(ref SlotOf(endpoint));
        return slot is not null && now < slot.Until + halfOpen;
    }

    /// <summary>The endpoints in back-off at <paramref name="now"/>, for the claim to leave out.</summary>
    /// <param name="now">The current time.</param>
    /// <returns>The endpoints whose rows are not to be claimed.</returns>
    public OutboxEndpoints BlockedAt(DateTimeOffset now)
    {
        var blocked = OutboxEndpoints.None;
        if (IsBlocked(OutboxDispatchers.Transport, now, out _))
        {
            blocked |= OutboxEndpoints.Transport;
        }

        if (IsBlocked(OutboxDispatchers.Local, now, out _))
        {
            blocked |= OutboxEndpoints.Local;
        }

        return blocked;
    }

    /// <summary>The tenants in back-off on <paramref name="endpoint"/>.</summary>
    /// <param name="endpoint">A dispatcher key.</param>
    /// <returns>That endpoint's tenant back-off.</returns>
    public TenantBackoff TenantsOf(string endpoint)
        => endpoint is OutboxDispatchers.Local ? _localTenants : _transportTenants;

    /// <summary>
    /// Records a failed attempt of <paramref name="endpoint"/> for every tenant and grows its back-off;
    /// the endpoint is suspected until it dispatches a row.
    /// </summary>
    /// <param name="endpoint">A dispatcher key.</param>
    /// <param name="now">The current time.</param>
    /// <returns>When the endpoint is tried again.</returns>
    public DateTimeOffset Block(string endpoint, DateTimeOffset now)
    {
        ref var field = ref SlotOf(endpoint);
        var previous = Volatile.Read(ref field);

        var delay = BackoffCurve.Next(previous?.Until, previous?.Delay ?? default, now, initial, ceiling);
        var slot = new Slot(now + delay, delay);
        Volatile.Write(ref field, slot);
        TenantsOf(endpoint).MarkSuspected();
        return slot.Until;
    }

    /// <summary>
    /// Records a row <paramref name="endpoint"/> dispatched: clears its back-off, and the back-off of
    /// the row's tenant on it.
    /// </summary>
    /// <param name="endpoint">A dispatcher key.</param>
    /// <param name="tenant">The row's tenant; <see langword="null"/> for none.</param>
    public void Succeeded(string endpoint, string? tenant)
    {
        ref var field = ref SlotOf(endpoint);
        if (Volatile.Read(ref field) is not null)
        {
            Volatile.Write(ref field, null);
        }

        TenantsOf(endpoint).Succeeded(tenant);
    }

    /// <summary>Removes the tenant entries expired for a whole ceiling, on both endpoints.</summary>
    /// <param name="now">The current time.</param>
    public void Prune(DateTimeOffset now)
    {
        _transportTenants.Prune(now);
        _localTenants.Prune(now);
    }

    private ref Slot? SlotOf(string endpoint)
    {
        if (endpoint is OutboxDispatchers.Local)
        {
            return ref _local;
        }

        return ref _transport;
    }

    private sealed record Slot(DateTimeOffset Until, TimeSpan Delay);
}
