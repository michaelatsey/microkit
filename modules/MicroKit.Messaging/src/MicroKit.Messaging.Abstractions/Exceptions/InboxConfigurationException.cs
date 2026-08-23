namespace MicroKit.Messaging;

/// <summary>
/// Thrown when a service the inbox requires is not registered. A deployment defect, never a
/// runtime fault.
/// </summary>
/// <remarks>
/// <para>
/// Raised by wrapping the resolution call itself, so the classification is structural rather
/// than heuristic. Inspecting <see cref="InvalidOperationException"/>'s message for a service
/// name would depend on text produced by the DI container — text that changes between versions
/// and containers — and would misclassify a genuine
/// <see cref="InvalidOperationException"/> thrown by a handler whose message happened to
/// mention the same type.
/// </para>
/// <para>
/// The processor settles the batch, releasing every row untouched, and only then rethrows, so
/// the hosting worker stops without stranding a single lease. Backing off on a missing
/// registration cannot succeed: it will not fix itself without a redeployment.
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
