namespace MicroKit.Messaging.Transport;

/// <summary>
/// The in-memory <see cref="IMessageTransport"/>: publishes on the <see cref="InMemoryBroker"/>,
/// which copies the envelope to every endpoint bound to its type.
/// </summary>
/// <remarks>
/// Returning means the broker accepted the envelope — every bound endpoint's queue holds a copy,
/// and the <see cref="InMemoryTransportWorker"/> of each endpoint delivers it. A type no endpoint is
/// bound to reaches no one, and that is logged at <c>Warning</c> (event 3014) rather than returned
/// as silence: a publisher need not know its subscribers, but nobody receiving is worth seeing.
/// </remarks>
internal sealed class InMemoryTransport : IMessageTransport
{
    private readonly InMemoryBroker _broker;
    private readonly ILogger<InMemoryTransport> _logger;

    /// <summary>Initializes the transport, binding this service's endpoint if it is not yet bound.</summary>
    /// <param name="broker">The broker.</param>
    /// <param name="binder">Binds the endpoint on first use — so a publication made before the host
    /// starts is held for this service's handlers.</param>
    /// <param name="logger">Logger.</param>
    /// <exception cref="IntegrationEventConfigurationException">The composition is invalid.</exception>
    public InMemoryTransport(
        InMemoryBroker broker, InMemoryEndpointBinder binder, ILogger<InMemoryTransport> logger)
    {
        ArgumentNullException.ThrowIfNull(binder);
        binder.EnsureBound();
        _broker = broker;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// A required member of the envelope — <c>Id</c>, <c>Source</c>, <c>Type</c>,
    /// <c>DataContentType</c> or <c>Headers</c> — is missing, so the malformed envelope fails at the
    /// sender rather than in the consume loop.
    /// </exception>
    public ValueTask SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        Validate(envelope);
        ct.ThrowIfCancellationRequested();

        if (_broker.Publish(envelope) == 0)
        {
            Bus.BusLogs.NoEndpointBound(_logger, envelope.Id, envelope.Type, envelope.Source);
        }

        return ValueTask.CompletedTask;
    }

    private static void Validate(MessageEnvelope envelope)
    {
        // `required` members are a compile-time promise only: `null!` and deserializers bypass it.
        Require(envelope.Id, nameof(MessageEnvelope.Id), nameof(envelope));
        Require(envelope.Source, nameof(MessageEnvelope.Source), nameof(envelope));
        Require(envelope.Type, nameof(MessageEnvelope.Type), nameof(envelope));
        Require(envelope.DataContentType, nameof(MessageEnvelope.DataContentType), nameof(envelope));

        if (envelope.Headers is null)
        {
            throw new ArgumentException(
                $"The envelope's {nameof(MessageEnvelope.Headers)} is null. Use an empty dictionary.",
                nameof(envelope));
        }
    }

    private static void Require(string? value, string member, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"The envelope's {member} is missing. Every envelope carries an id, a source, a type " +
                "and a content type.",
                paramName);
        }
    }
}
