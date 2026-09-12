namespace MicroKit.Messaging;

/// <summary>
/// Thrown when a service the inbox requires is not registered. A deployment defect, never a
/// runtime fault.
/// </summary>
/// <remarks>
/// <para>
/// <b>It means UNREGISTERED, and nothing else.</b> A service that is registered and fails to
/// activate is not this: that raises whatever its activation raised, and it propagates unchanged
/// to be classified by the path it happened on. The distinction is load-bearing under a
/// tenant-aware <c>IExecutionScopeFactory</c>, where both the inbox writer and the
/// settlement store activate the consumer's <c>DbContext</c> and an envelope naming an unknown
/// tenant fails the per-tenant connection resolution with exactly
/// <see cref="InvalidOperationException"/> — one message's data fault, which must not be reported
/// as a composition that no redelivery can fix.
/// </para>
/// <para>
/// <b>Raised from two places, and the remediation differs because only one of them holds rows.</b>
/// <list type="bullet">
///   <item><b>The drain path</b> — <c>InboxProcessor</c> resolves the message handler and
///         <see cref="IInboxSettlementStore"/> from the per-message execution scope. It settles the
///         batch first, releasing every row untouched and consuming no retry, and only then
///         rethrows, so the hosting worker stops without stranding a single lease. Backing off
///         cannot succeed: a missing registration will not fix itself without a redeployment, and
///         a worker that quietly retries forever hides the defect.</item>
///   <item><b>The ingestion path</b> — <see cref="IEnvelopeReceiver"/> resolves
///         <see cref="IInboxWriter"/>, without which an arriving message cannot be recorded at all.
///         <b>There is no batch, no lease and no row here, so the paragraph above does not
///         apply</b>: nothing has been claimed and nothing needs releasing. The verdict reaches a
///         broker provider instead, which should let it stop the consume loop rather than nack a
///         message forever against a composition no redelivery can fix.</item>
/// </list>
/// </para>
/// <para>
/// <b>Detection is structural at every site: <c>GetService</c> plus a null test.</b> Two
/// alternatives are rejected. Inspecting <see cref="InvalidOperationException"/>'s message for a
/// service name depends on text produced by the DI container — text that changes between versions
/// and containers — and would misclassify a genuine
/// <see cref="InvalidOperationException"/> thrown by a handler whose message happened to mention
/// the same type. Wrapping <c>GetRequiredService</c> in a <c>catch</c> is the subtler error: it
/// reads as a registration lookup while in fact spanning the whole activation graph behind the
/// registration, which is how an activation failure becomes a reported missing registration.
/// <c>GetService</c> returns <see langword="null"/> only when nothing is registered.
/// </para>
/// </remarks>
public sealed class InboxConfigurationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InboxConfigurationException"/> class.
    /// </summary>
    public InboxConfigurationException()
        : base("A service the inbox requires is not registered.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxConfigurationException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public InboxConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxConfigurationException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The underlying resolution fault.</param>
    public InboxConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
