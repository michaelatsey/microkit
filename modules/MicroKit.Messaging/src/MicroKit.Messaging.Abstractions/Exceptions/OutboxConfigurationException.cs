namespace MicroKit.Messaging;

/// <summary>
/// Thrown when the outbox cannot dispatch because of how the application is composed, rather than
/// because of anything about the message or the moment. A deployment defect, never a runtime fault.
/// </summary>
/// <remarks>
/// <para>
/// <b>Raised from two kinds of place, both of which establish the fault before any delivery is
/// attempted.</b>
/// <list type="bullet">
///   <item><b>Service resolution, when nothing is registered</b> — <c>OutboxProcessor</c>
///         resolves <see cref="IOutboxDispatcher"/>, and the scope's origin holder, with
///         <c>GetService</c>, and raises this only when the container returns
///         <see langword="null"/>: the service is not registered at all.</item>
///   <item><b>A dispatcher handed a row it structurally cannot serve in this composition</b> —
///         <c>TransportOutboxDispatcher</c> raises it for a <see cref="MessageKind.Notification"/>
///         row, which nothing in the process can fan out unless
///         <c>MicroKit.Messaging.MediatR</c> is registered; <c>MediatROutboxDispatcher</c> raises it
///         for any other row when no standard dispatcher is registered behind it to take the row. The
///         row is valid and the payload is fine; its arrival is what proves a registration is
///         missing.</item>
/// </list>
/// A <see cref="IMessageTransport"/> implementation must <b>not</b> raise it — see that
/// interface's remarks. By the time a transport runs, the composition has already been proven
/// adequate by the fact that the transport was resolved and called.
/// </para>
/// <para>
/// <b>Not raised for a dispatcher that is registered but cannot be activated.</b> A
/// <c>TransportOutboxDispatcher</c> with no <see cref="IMessageTransport"/> behind it is the common
/// case: the container throws <see cref="InvalidOperationException"/> while activating the graph, and
/// that is a different verdict. The message that met it and every one after it are released with no
/// retry consumed, nothing is rethrown, the batch result reports
/// <see cref="OutboxBatchAbortReason.DispatcherActivationFailed"/>, and the next cycle retries. The
/// worker keeps running, so a transient cause drains the queue once it passes, and a missing
/// dependency drains it once a build that supplies it is deployed. Only that exception type gets this
/// verdict: any other thrown while the graph is built, this one included, is classified as if the
/// dispatcher had thrown it.
/// </para>
/// <para>
/// <b>Not every such cause passes.</b> The dispatcher is activated in the scope of the message being
/// dispatched, built from that message's tenant, so a tenant-aware composition can throw that
/// exception for one tenant only — and a de-provisioned tenant never recovers. Its row stays oldest
/// and heads every claim, and the outbox stalls for every tenant, with no dead-letter exit, until an
/// operator intervenes. That is
/// a known defect, not a design property; ADR-MSG-019 records it and the fix owed.
/// </para>
/// <para>
/// <b>The two are separated by the container, not by inspecting an exception.</b>
/// <c>GetService</c> returns <see langword="null"/> only when nothing is registered, and throws when
/// something is registered and cannot be activated. Inspecting
/// <c>InvalidOperationException.Message</c> for a service name instead is rejected: it depends on
/// text produced by the DI container — text that changes between versions and containers — and
/// would misclassify a genuine <see cref="InvalidOperationException"/> thrown by the dispatcher
/// whose message happened to mention the same type. So is reading an
/// <see cref="InvalidOperationException"/> from the resolution as a missing registration, as a
/// <c>catch</c> around <c>GetRequiredService</c> did: it spans the whole activation graph while
/// reading as a registration lookup.
/// </para>
/// <para>
/// The processor settles the batch — the message that raised this and every one after it are
/// released untouched, while those dispatched before it keep their outcome — and then rethrows, so
/// the hosting worker stops. Backing off and retrying a missing registration cannot succeed: it will
/// not fix itself without a redeployment, and a worker that quietly retries forever hides the defect.
/// </para>
/// </remarks>
public sealed class OutboxConfigurationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="OutboxConfigurationException"/> class.</summary>
    public OutboxConfigurationException()
        : base("A service required by the outbox is not registered.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OutboxConfigurationException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    public OutboxConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OutboxConfigurationException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that establishes the fault.</param>
    public OutboxConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
