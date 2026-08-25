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
///   <item><b>Service resolution</b> — <c>OutboxProcessor</c> wraps the container call that
///         activates <see cref="IOutboxDispatcher"/>, so the classification is structural rather
///         than heuristic. That covers a missing dispatcher <i>and</i> a missing dependency of
///         one: a registered <c>TransportOutboxDispatcher</c> with no
///         <see cref="IMessageTransport"/> behind it fails exactly here, which is the reason the
///         transport is a constructor dependency rather than a lazy lookup.</item>
///   <item><b>A dispatcher handed a row it structurally cannot serve in this composition</b> —
///         <c>TransportOutboxDispatcher</c> raises it for a <see cref="MessageKind.Notification"/>
///         row, which nothing in the process can fan out unless
///         <c>MicroKit.Messaging.MediatR</c> is registered. The row is valid and the payload is
///         fine; its arrival is what proves the package is missing.</item>
/// </list>
/// A <see cref="IMessageTransport"/> implementation must <b>not</b> raise it — see that
/// interface's remarks. By the time a transport runs, the composition has already been proven
/// adequate by the fact that the transport was resolved and called.
/// </para>
/// <para>
/// Inspecting <c>InvalidOperationException.Message</c> for a service name would be the
/// alternative to the structural detection above, and is rejected: it depends on text produced by
/// the DI container — text that changes between versions and containers — and would misclassify a
/// genuine <see cref="InvalidOperationException"/> thrown by the dispatcher whose message happened
/// to mention the same type.
/// </para>
/// <para>
/// The processor settles the batch (releasing every message untouched) and then
/// rethrows, so the hosting worker stops. Backing off and retrying a missing
/// registration cannot succeed: it will not fix itself without a redeployment, and a
/// worker that quietly retries forever hides the defect.
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
    /// <param name="innerException">The resolution failure this wraps.</param>
    public OutboxConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
