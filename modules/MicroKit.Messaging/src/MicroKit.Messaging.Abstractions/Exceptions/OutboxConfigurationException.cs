namespace MicroKit.Messaging;

/// <summary>
/// Thrown when a service the outbox requires is not registered. A deployment defect,
/// never a runtime fault.
/// </summary>
/// <remarks>
/// <para>
/// Raised by wrapping the resolution call itself, so the classification is structural
/// rather than heuristic. Inspecting <c>InvalidOperationException.Message</c> for a
/// service name would depend on text produced by the DI container — text that changes
/// between versions and containers — and would misclassify a genuine
/// <see cref="InvalidOperationException"/> thrown by the dispatcher whose message
/// happened to mention the same type.
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
