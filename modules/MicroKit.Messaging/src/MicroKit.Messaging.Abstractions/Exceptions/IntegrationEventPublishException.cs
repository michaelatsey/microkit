namespace MicroKit.Messaging;

/// <summary>
/// Thrown when <see cref="IIntegrationEventPublisher.PublishAsync{TEvent}"/> is called with no
/// open transaction on the caller's unit of work.
/// </summary>
/// <remarks>
/// <para>
/// <b>Refusing loudly is the only way this failure is ever noticed.</b> A row staged with no
/// transaction to commit it is not an error anyone sees: the change tracker is discarded, the
/// event never existed, and no log, metric or trace records that it was meant to. The publication
/// silently did not happen.
/// </para>
/// <para>
/// A handler that publishes an integration event must be inside a unit of work it opened. Note
/// that committing is not the same as opening one: <c>IUnitOfWork.CommitAsync</c> is a bare
/// <c>SaveChangesAsync</c> running under the provider's implicit per-call transaction, which never
/// appears as an open transaction on the context. Wrap the work in
/// <c>ITransactionalContext.ExecuteAsync</c> and publish inside that callback — see the example on
/// <see cref="IIntegrationEventPublisher"/>.
/// </para>
/// <para>
/// The guard runs before any other work, so a rejected publication stages nothing.
/// </para>
/// </remarks>
public sealed class IntegrationEventPublishException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="IntegrationEventPublishException"/> class.</summary>
    public IntegrationEventPublishException()
        : base("An integration event was published with no open transaction.")
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
