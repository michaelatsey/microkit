using MediatR;
using MicroKit.Abstractions.Serialization;
using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Outbox;
using System.Collections.Concurrent;

namespace MicroKit.Messaging.Publisher.MediatR;

/// <summary>Publishes outbox messages via MediatR using type-safe deserialization.</summary>
internal sealed class MediatROutboxPublisher : IOutboxPublisher
{
    private readonly IPublisher _publisher;
    private readonly IMicroKitSerializer _serializer;
    private readonly IMicroKitMessageContextSetter _contextSetter;
    private readonly IMessageTypeRegistry _typeRegistry;
    private static readonly ConcurrentDictionary<string, Type> EnvelopeTypeCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of <see cref="MediatROutboxPublisher"/>.
    /// </summary>
    public MediatROutboxPublisher(
        IPublisher publisher,
        IMicroKitSerializer serializer,
        IMicroKitMessageContextSetter contextSetter,
        IMessageTypeRegistry typeRegistry)
    {
        _publisher = publisher;
        _serializer = serializer;
        _contextSetter = contextSetter;
        _typeRegistry = typeRegistry;
    }

    /// <inheritdoc />
    public async Task PublishAsync(OutboxMessage storedMessage, CancellationToken cancellationToken = default)
    {
        var envelopeType = ResolveEnvelopeType(storedMessage.MessageType)
            ?? throw new InvalidOperationException(
                $"Cannot resolve message type '{storedMessage.MessageType}'. " +
                "Register it via IMessageTypeRegistry or ensure its assembly is loaded.");

        var envelopeObj = _serializer.Deserialize(storedMessage.Payload, envelopeType)
            ?? throw new InvalidOperationException($"Failed to deserialize OutboxMessage payload for type '{storedMessage.MessageType}'.");

        var envelope = (EventEnvelopeBase)envelopeObj;

        using (_contextSetter.SetContext(
            envelope.TenantId,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.IdempotencyKey))
        {
            if (storedMessage.PublishAsNotification)
            {
                var payload = envelope.GetPayload();

                if (payload is not INotification notification)
                    throw new InvalidOperationException(
                        $"Payload of type '{payload.GetType().FullName}' does not implement INotification. " +
                        "Only INotification payloads can be published via MediatR.");

                await _publisher.Publish(notification, cancellationToken);
            }
        }
    }

    private Type? ResolveEnvelopeType(string messageTypeName)
    {
        return EnvelopeTypeCache.GetOrAdd(messageTypeName, typeName =>
        {
            // Try registry first (explicit registration, no AppDomain scan needed)
            var registeredType = _typeRegistry.Resolve(typeName);
            if (registeredType != null)
            {
                // If caller registered the payload type, wrap it in EventEnvelope<T>
                if (registeredType.IsGenericType && registeredType.GetGenericTypeDefinition() == typeof(EventEnvelope<>))
                    return registeredType;

                return typeof(EventEnvelope<>).MakeGenericType(registeredType);
            }

            // Fallback: scan loaded assemblies (e.g. payload type registered by full name)
            var payloadType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName))
                .FirstOrDefault(t => t != null);

            if (payloadType == null)
                throw new InvalidOperationException(
                    $"Message type '{typeName}' not found in any loaded assembly. " +
                    "Register it explicitly via IMessageTypeRegistry.");

            return typeof(EventEnvelope<>).MakeGenericType(payloadType);
        });
    }
}
