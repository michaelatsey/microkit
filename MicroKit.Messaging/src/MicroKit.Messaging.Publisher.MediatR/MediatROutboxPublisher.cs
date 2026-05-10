using MediatR;
using MicroKit.Abstractions.Serialization;
using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Outbox;
using System.Collections.Concurrent;
using System.Text.Json;

namespace MicroKit.Messaging.Publisher.MediatR;

internal class MediatROutboxPublisher : IOutboxPublisher
{
    private readonly IPublisher _publisher;
    private readonly IMicroKitSerializer _serializer;
    private readonly IMicroKitMessageContextSetter _contextSetter;
    private static readonly ConcurrentDictionary<string, Type?> EnvelopeTypeCache = new();
    public MediatROutboxPublisher(IPublisher publisher, IMicroKitSerializer serializer, IMicroKitMessageContextSetter contextSetter)
    {
        _publisher = publisher;
        _serializer = serializer;
        _contextSetter = contextSetter;
    }
    public async Task PublishAsync(OutboxMessage storedMessage, CancellationToken cancellationToken = default)
    {
        
        // 1. Résoudre le type de l'objet à partir de la chaîne MessageType
        var envelopType = GetEnvelopeType(storedMessage.MessageType) 
            ?? throw new InvalidOperationException($"Impossible de résoudre le type de payload : {storedMessage.MessageType}");

        var envelope = (dynamic)_serializer.Deserialize(storedMessage.Payload, envelopType)!
            ?? throw new InvalidOperationException("Failed to deserialize OutboxMessage");

        using (_contextSetter.SetContext(
            envelope.TenantId, 
            envelope.CorrelationId, 
            envelope.CausationId, 
            envelope.IdempotencyKey))
        {
            // 2. Désérialiser le payload vers le type concret
            if (storedMessage.PublishAsNotification)
            {
                //if (payload is not INotification mediatrNotification)
                //{
                //    throw new InvalidOperationException($"Le payload {storedMessage.MessageId} n'implémente pas INotification.");
                //}
                // 3. Publier via MediatR
                // Note : On pourrait enrichir ici un AsyncLocal ou un Scope avec le CorrelationId si besoin
                await _publisher.Publish(envelope.Payload, cancellationToken);
            }
        }
        

        
        
    }

    private static Type? GetEnvelopeType(string payloadTypeName)
    {
        return EnvelopeTypeCache.GetOrAdd(payloadTypeName, typeName =>
        {
            // Résolution du type concret (ex: MonProjet.OrderCreated)
            var payloadType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName))
                .FirstOrDefault(t => t != null) 
                ?? throw new InvalidOperationException($"Le type de message '{typeName}' est introuvable dans les assemblies chargés.");

            // Construction du type EventEnvelope<PayloadType>
            return typeof(EventEnvelope<>).MakeGenericType(payloadType);
        });
    }


}
