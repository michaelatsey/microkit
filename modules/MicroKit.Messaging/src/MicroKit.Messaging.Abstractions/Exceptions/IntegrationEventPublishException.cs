namespace MicroKit.Messaging;

/// <summary>
/// Thrown by <see cref="IEventPublisher.PublishAsync{T}"/> when a publication is refused before
/// anything is sent or enlisted.
/// </summary>
/// <remarks>
/// <para>Raised for a publication that would otherwise succeed silently or land in the wrong place:</para>
/// <list type="bullet">
/// <item><description>an explicit tenant that differs from the ambient one;</description></item>
/// <item><description>
/// a <see cref="PublishOptions.Store"/> that names no configured store, or, inside a delivery, a
/// store other than that delivery's;
/// </description></item>
/// <item><description>a send middleware that completed without calling the rest of the pipeline.</description></item>
/// </list>
/// <para>Nothing is sent and nothing is enlisted when it is thrown.</para>
/// </remarks>
public sealed class IntegrationEventPublishException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="IntegrationEventPublishException"/> class.</summary>
    public IntegrationEventPublishException()
        : base("An integration event publication was refused.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IntegrationEventPublishException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    public IntegrationEventPublishException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IntegrationEventPublishException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The underlying fault.</param>
    public IntegrationEventPublishException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
