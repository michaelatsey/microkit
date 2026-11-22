namespace MicroKit.Messaging.Processing;

/// <summary>
/// Why the batch did not run every claimed message to an outcome of its own: it stopped (cancellation,
/// a configuration fault), or an endpoint was down and its later rows were deferred (#188). The first endpoint verdict the
/// batch met, unless the batch then stopped — cancellation or a configuration fault takes precedence;
/// why an endpoint is down is logged with it (1004, 1009), and what each endpoint deferred is event
/// 1010 — the only event for <see cref="EndpointInBackOff"/> — once the batch is fully settled; a
/// partial or failed settlement is reported by its own event instead.
/// </summary>
/// <remarks>
/// Members may be added in a later version. A consumer switching over this type should give unknown
/// values a default arm rather than treat the switch as exhaustive, and should test
/// <see cref="Cancelled"/> and <see cref="OutboxBatchResult.HandedOverInBackOff"/> before
/// <see cref="OutboxBatchResult.IsSaturated"/>: a released row can be claimed at once, and a
/// store that does not honour the claim's endpoint filter keeps handing over the down endpoint's
/// backlog, a batch at a time.
/// </remarks>
internal enum OutboxBatchAbortReason
{
    /// <summary>Every claimed message was attempted.</summary>
    None = 0,

    /// <summary>
    /// An endpoint was unreachable — the transport, or a local consumer's dependency
    /// (<see cref="OutboxTransportUnavailableException"/> either way). Its rows were deferred to the end
    /// of the endpoint's back-off; it may recover on its own.
    /// </summary>
    TransportUnavailable = 1,

    /// <summary>The host is shutting down. Not an error.</summary>
    Cancelled = 2,

    /// <summary>
    /// A required service is not registered. Will not recover without a redeployment: the loop
    /// backs off and reports it (1402) on every cycle — only a host stop ends a loop.
    /// </summary>
    ConfigurationError = 3,

    /// <summary>
    /// An <see cref="IOutboxDispatcher"/> is registered, but the container threw
    /// <see cref="InvalidOperationException"/> while activating it — most often because a dependency
    /// such as <see cref="IMessageTransport"/> is not registered. The message that met the fault was
    /// deferred, and its endpoint's later messages too while the back-off was in force, with no retry
    /// consumed; the other endpoint's were still dispatched. Nothing was rethrown: the endpoint is tried again once its back-off ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cause that passes on its own drains the queue unaided. A missing dependency drains it once a
    /// build that supplies it is deployed. Any other exception type thrown during activation is not
    /// reported here: it is classified as if the dispatcher had thrown it.
    /// </para>
    /// <para>
    /// <b>Not every cause passes.</b> The dispatcher is activated in the scope of the message being
    /// dispatched, built from its tenant, so the cause can be one tenant's. On a row with a tenant, the
    /// verdict holds that tenant on the endpoint, not the endpoint (#124): its rows wait out its own
    /// back-off and every other tenant's go out. One that never clears, such as a de-provisioned tenant,
    /// is tried once per its back-off and never dead-letters on its own: its persisting is an incident,
    /// and <c>IOutboxAdminStore.DeadLetterPendingAsync</c> is the way out (module README, <i>One tenant
    /// down</i>).
    /// </para>
    /// </remarks>
    DispatcherActivationFailed = 4,

    /// <summary>
    /// Rows of an endpoint already in back-off were handed over — because the back-off began after the
    /// claim, or by a store that does not honour the claim's endpoint filter — and deferred
    /// unattempted.
    /// </summary>
    EndpointInBackOff = 5,
}
