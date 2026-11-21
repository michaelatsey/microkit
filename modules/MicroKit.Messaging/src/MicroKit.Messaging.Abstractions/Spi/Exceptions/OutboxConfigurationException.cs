namespace MicroKit.Messaging.Spi;

/// <summary>
/// Thrown when the outbox cannot dispatch because of how the application is composed, rather than
/// because of anything about the message or the moment. A deployment defect, never a runtime fault.
/// </summary>
/// <remarks>
/// <para>
/// Part of the SPI (<c>MicroKit.Messaging.Spi</c>), but <b>the engine raises this one, not a
/// provider.</b> ⚠ A transport or store implementation must <b>never</b> throw it: its documented
/// effect is that the worker <i>stops</i>, so raising it from a broker client turns a transient
/// outage into an outbox that drains nothing until someone redeploys. By the time a transport
/// runs, the composition has already been proven adequate. Use
/// <see cref="OutboxTransportUnavailableException"/> for an unreachable broker, or let an untyped
/// exception propagate for an ordinary transient fault.
/// </para>
/// <para>
/// <b>Raised by service resolution, before any delivery is attempted.</b> <c>OutboxProcessor</c>
/// resolves the dispatcher of each row's endpoint with <c>GetService</c>, and raises this only when
/// the container returns <see langword="null"/>: the dispatcher is not registered at all. Every store
/// registers both dispatchers, so that is a container composed outside the store engine; a host with
/// no transport meets the activation failure instead. The row is valid; its arrival is what proves a
/// registration is missing.
/// A <see cref="IMessageTransport"/> implementation must <b>not</b> raise it — see that
/// interface's remarks. By the time a transport runs, the composition has already been proven
/// adequate by the fact that the transport was resolved and called.
/// </para>
/// <para>
/// <b>Not raised for a dispatcher that is registered but cannot be activated.</b> A
/// <c>TransportOutboxDispatcher</c> with no <see cref="IMessageTransport"/> behind it is the common
/// case: the container throws <see cref="InvalidOperationException"/> while activating the graph, and
/// that is a different verdict. The message's endpoint is put in back-off: the message that met it
/// and that endpoint's later rows are deferred with no retry consumed, the endpoint is left out of the
/// claim until its back-off ends, nothing is rethrown, the batch result reports
/// <c>OutboxBatchAbortReason.DispatcherActivationFailed</c>, and the other endpoint's rows are still
/// dispatched. A transient cause drains the queue once it passes, and a missing dependency drains it
/// once a build that supplies it is deployed. Only that exception type gets this
/// verdict: any other thrown while the graph is built, this one included, is classified as if the
/// dispatcher had thrown it.
/// </para>
/// <para>
/// <b>Not every such cause passes.</b> The dispatcher is activated in the scope of the message being
/// dispatched, built from that message's tenant, so a tenant-aware composition can throw that
/// exception for one tenant only — and a de-provisioned tenant never recovers. Each time the
/// endpoint's back-off ends its row fails again, and that endpoint's rows of every tenant wait, with
/// no dead-letter exit, until an operator intervenes; the other endpoint's rows still go out. That is
/// a known defect, not a design property; the fix is owed by #124.
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
