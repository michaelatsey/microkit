namespace MicroKit.Messaging;

/// <summary>Why a batch stopped before every claimed message was attempted.</summary>
/// <remarks>
/// Members may be added in a later version. A consumer switching over this type should give unknown
/// values a default arm rather than treat the switch as exhaustive, and should test the reason before
/// <see cref="OutboxBatchResult.IsSaturated"/>: an aborted batch still counts every message it claimed.
/// </remarks>
public enum OutboxBatchAbortReason
{
    /// <summary>Every claimed message was attempted.</summary>
    None = 0,

    /// <summary>The transport was unreachable. Back off hard; it may recover on its own.</summary>
    TransportUnavailable = 1,

    /// <summary>The host is shutting down. Not an error.</summary>
    Cancelled = 2,

    /// <summary>
    /// A required service is not registered. Will not recover without a redeployment,
    /// so the worker stops rather than backing off.
    /// </summary>
    ConfigurationError = 3,

    /// <summary>
    /// An <see cref="IOutboxDispatcher"/> is registered, but the container threw
    /// <see cref="InvalidOperationException"/> while activating it — most often because a dependency
    /// such as <see cref="IMessageTransport"/> is not registered. The message that met the fault and
    /// every message after it were released with no retry consumed; messages dispatched before it keep
    /// their outcome. Nothing was rethrown: the worker backs off and the next cycle retries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cause that passes on its own drains the queue unaided. A missing dependency drains it once a
    /// build that supplies it is deployed. Any other exception type thrown during activation is not
    /// reported here: it is classified as if the dispatcher had thrown it.
    /// </para>
    /// <para>
    /// ⚠ <b>Known defect — not every cause passes.</b> The dispatcher is activated in the scope of the
    /// message being dispatched, built from its tenant, so the cause can be one tenant's. One that never
    /// clears, such as a de-provisioned tenant, keeps that message oldest: it heads every claim, every
    /// batch reports this value, no tenant publishes, and nothing dead-letters. Treat this value
    /// persisting across cycles as an incident, not as back-pressure. ADR-MSG-019 records the defect
    /// and the fix owed; the module README gives the way out.
    /// </para>
    /// </remarks>
    DispatcherActivationFailed = 4,
}
