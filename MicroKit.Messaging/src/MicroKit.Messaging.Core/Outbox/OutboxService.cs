using MicroKit.Abstractions.Serialization;
using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Outbox;
using MicroKit.MultiTenancy;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace MicroKit.Messaging.Core.Outbox;

/// <summary>
/// Réprésente le service de la boîte d'envoi (Outbox) pour la gestion des messages à envoyer, fournissant des méthodes pour enqueuer des messages individuels ou en batch, et interagissant avec le référentiel de la boîte d'envoi pour stocker et récupérer les messages à envoyer, tout en assurant une journalisation appropriée des opérations liées à la boîte d'envoi.
/// </summary>
/// <seealso cref="MicroKit.Messaging.Abstractions.Outbox.IOutboxService" />
public class OutboxService: IOutboxService
{
    /// <summary>
    /// The repository
    /// </summary>
    private readonly IOutboxRepository _repository;

    private readonly IMicroKitSerializer _serializer;

    /// <summary>
    /// The logger
    /// </summary>
    private readonly ILogger<OutboxService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxService"/> class.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="serializer">The serializer.</param>
    public OutboxService(
        IOutboxRepository repository,
        ILogger<OutboxService> logger,
        IMicroKitSerializer serializer)
    {
        _repository = repository;
        _logger = logger;
        _serializer = serializer;
    }

    /// <summary>
    /// Surcharge générique pour faciliter l'utilisation par les développeurs
    /// </summary>
    public async Task<string> EnqueueAsync<T>(
        string tenantId,
        string messageId,
        T payload,
        OutboxDestination destination,
        string? correlationId = null,
        string? causationId = null,
        string? idempotencyKey = null,
        CancellationToken ct = default) where T : class
    {
        // Utiliser le nom complet pour éviter les ambiguïtés au Publisher
        var messageType = payload.GetType().FullName!;

        // CRÉATION DE L'ENVELOPPE
        var envelope = new EventEnvelope<T>
        {
            EventId = messageId,
            TenantId = tenantId,
            MessageType = messageType,
            Payload = payload,
            OccurredOnUtc = DateTimeOffset.UtcNow,
            CorrelationId = correlationId,
            CausationId = causationId,
            IdempotencyKey = idempotencyKey,
            Metadata = destination.Metadata
        };
        var df = _serializer.Serialize(payload); 
        // SÉRIALISATION DE L'ENVELOPPE COMPLÈTE
        // Le Payload de la base de données contiendra le JSON de l'enveloppe entière
        var jsonPayload = _serializer.Serialize(envelope);

        return await EnqueueAsync(
            messageId.ToString(),
            tenantId,
            messageType,
            jsonPayload,
            destination,
            correlationId,
            causationId,
            idempotencyKey,
            ct);
        
        
    }

    /// <summary>
    /// Enqueues the asynchronous.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="messageType">Type of the message.</param>
    /// <param name="payload">The payload.</param>
    /// <param name="destination">The destination.</param>
    /// <param name="correlationId">The correlation identifier.</param>
    /// <param name="causationId">The causation identifier.</param>
    /// <param name="idempotencyKey"></param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    public async Task<string> EnqueueAsync(
        string messageId,
        string tenantId,
        string messageType,
        string payload, // eveloppe
        OutboxDestination destination,
        string? correlationId = null,
        string? causationId = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        // On prépare l'objet de destination finale

        // Création de l'entité de stockage (OutboxMessage)
        // C'est ici que l'on aplatit les infos de traçabilité (Correlation/Causation)
        var message = new OutboxMessage
        {
            Id = messageId,
            TenantId = tenantId,
            MessageType = messageType,
            Payload = payload,

            // Propriétés aplaties
            PublishAsNotification = destination.PublishAsNotification,
            PublishToBroker = destination.PublishToBroker,
            BrokerTopic = destination.BrokerTopic,
            PartitionKey = destination.PartitionKey,
            DestinationMetadata = destination.Metadata != null
            ? JsonSerializer.Serialize(destination.Metadata)
            : null,

            CorrelationId = correlationId,
            CausationId = causationId,
            IdempotencyKey = idempotencyKey,

            Status = MessageStatus.Pending,
            OccurredOnUtc = DateTimeOffset.UtcNow,
            RetryCount = 0
        };


        // Validation (On pourrait injecter un IValidator ici)
        ValidateMessage(message);

        await _repository.AddAsync(message, cancellationToken);

        if(_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Message {MessageId} of type {MessageType} enqueued to outbox",
                message.Id, messageType);
        }
        

        return message.Id;
    }

    /// <summary>
    /// Enqueues the batch asynchronous.
    /// </summary>
    /// <param name="messages">The messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    public async Task<IEnumerable<string>> EnqueueBatchAsync(
        IEnumerable<OutboxMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        // On matérialise et on prépare les messages en une seule passe
        var preparedMessages = messages.Select(m => {
            m.Id = string.IsNullOrEmpty(m.Id) ? Guid.NewGuid().ToString() : m.Id;
            m.Status = MessageStatus.Pending;
            m.OccurredOnUtc = now;
            return m;
        }).ToList();

        if (preparedMessages.Count == 0) return [];

        // Insertion groupée dans le ChangeTracker
        await _repository.AddRangeAsync(preparedMessages, cancellationToken);

        if(_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Enqueued batch of {Count} messages in outbox", preparedMessages.Count);
        }

        return preparedMessages.Select(m => m.Id.ToString());
    }

    private static void ValidateMessage(OutboxMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Payload))
            throw new ArgumentException("Payload cannot be empty", nameof(message));

        // On vérifie qu'au moins un canal de publication est activé
        if (!message.PublishAsNotification && !message.PublishToBroker)
        {
            throw new ArgumentException("At least one destination (Notification or Broker) must be enabled", nameof(message));
        }

        // Si on publie sur un Broker, le topic est généralement obligatoire
        if (message.PublishToBroker && string.IsNullOrWhiteSpace(message.BrokerTopic))
        {
            throw new ArgumentException("BrokerTopic is required when PublishToBroker is true", nameof(message.BrokerTopic));
        }
    }
}
