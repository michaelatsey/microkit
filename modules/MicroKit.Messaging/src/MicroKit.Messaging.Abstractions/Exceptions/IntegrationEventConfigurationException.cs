namespace MicroKit.Messaging;

/// <summary>
/// Thrown when integration event publication is misconfigured: an event type that is not
/// registered, a contract name claimed twice, or an event declared without
/// <see cref="IntegrationEventAttribute"/>. A deployment defect, never a runtime fault.
/// </summary>
/// <remarks>
/// <para>
/// Matches the module convention alongside <see cref="OutboxConfigurationException"/> and
/// <see cref="InboxConfigurationException"/>. The sibling categories those two have — permanent
/// payload failure, dependency unavailable — do not exist on this path: staging performs no I/O
/// and touches no payload it did not just serialize, so it has no transient failure mode and
/// nothing to be unavailable.
/// </para>
/// <para>
/// Composition is validated eagerly at startup, so a duplicated contract name fails at boot rather
/// than inside the one notification handler that emits that event — where it would roll back a
/// transaction and be classified as a transient failure, retrying forever against something no
/// retry can fix.
/// </para>
/// </remarks>
public sealed class IntegrationEventConfigurationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="IntegrationEventConfigurationException"/> class.</summary>
    public IntegrationEventConfigurationException()
        : base("Integration event publication is misconfigured.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IntegrationEventConfigurationException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    public IntegrationEventConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IntegrationEventConfigurationException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The underlying configuration fault.</param>
    public IntegrationEventConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
