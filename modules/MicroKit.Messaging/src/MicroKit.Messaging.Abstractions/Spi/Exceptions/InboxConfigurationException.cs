namespace MicroKit.Messaging.Spi;

/// <summary>
/// Thrown when a service the inbox requires is not registered. A deployment defect, never a
/// runtime fault.
/// </summary>
/// <remarks>
/// <para>
/// Part of the SPI (<c>MicroKit.Messaging.Spi</c>), but <b>the engine raises this one, not a
/// provider</b> — a store's inbox service is missing from the container. It stops that store's
/// retry loop, so a provider must not raise it for a runtime fault. A store author may throw it for a
/// genuine registration defect it alone can detect; for anything that could clear on its own, use
/// <see cref="InboxDependencyUnavailableException"/> or let an untyped exception propagate.
/// </para>
/// <para>
/// <b>It means UNREGISTERED, and nothing else.</b> A service that is registered and fails to
/// activate is not this: that raises whatever its activation raised, and it propagates unchanged
/// to be classified by the path it happened on.
/// </para>
/// <para>
/// <b>Raised for a store whose <see cref="IInboxGuardStore"/> is not registered under its key</b>
/// (#188), which a host start already rejects. On a store's retry loop the batch is settled first,
/// every row released with no attempt counted, then the loop of that store stops. On a delivery no
/// row is written and the message is redelivered.
/// </para>
/// <para>
/// <b>Detection is structural: <c>GetService</c> plus a null test.</b> Wrapping
/// <c>GetRequiredService</c> in a <c>catch</c> would read as a registration lookup while spanning
/// the whole activation graph behind the registration, which is how an activation failure becomes
/// a reported missing registration. <c>GetService</c> returns <see langword="null"/> only when
/// nothing is registered.
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
