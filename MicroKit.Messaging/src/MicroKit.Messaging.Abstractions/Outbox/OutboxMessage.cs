using MicroKit.Messaging.Abstractions.Common;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace MicroKit.Messaging.Abstractions.Outbox;

/// <summary>
/// Represente une entrée dans la table d'outbox, utilisée pour stocker les messages avant de les envoyer
/// </summary>
/// <seealso cref="MicroKit.Messaging.Abstractions.Common.IMessage" />
public class OutboxMessage : IMessage
{
    public string Id { get; set; } = null!;  // => MessageId
    public string TenantId { get; set; } = null!;  // tenant identifier
    public string MessageType { get; set; } = null!;
    public string Payload { get; set; } = null!;

    // Données de l'enveloppe aplaties pour faciliter les requêtes
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public string? IdempotencyKey { get; set; }

    // --- PROPRIÉTÉS DE DESTINATION (APPLATIES ICI) ---
    public bool PublishAsNotification { get; set; }
    public bool PublishToBroker { get; set; }
    public string? BrokerTopic { get; set; }
    public string? PartitionKey { get; set; }

    // Le dictionnaire est stocké en chaîne JSON simple
    public string? DestinationMetadata { get; set; }

    [NotMapped] // Ne sera pas en base, seulement pour ton code C#
    public Dictionary<string, string> Metadata
    {
        get => string.IsNullOrEmpty(DestinationMetadata)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string>>(DestinationMetadata)!;
        set => DestinationMetadata = JsonSerializer.Serialize(value);
    }

    public DateTimeOffset OccurredOnUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public DateTimeOffset? ScheduledAtUtc { get; set; }

    public DateTimeOffset? LockedUntilUtc { get; set; }
    public DateTimeOffset? LastAttemptedAtUtc { get; set; }

    public MessageStatus Status { get; set; } = MessageStatus.Pending;

    public int RetryCount { get; set; }
    public string? Error { get; set; }
    public byte[] RowVersion { get; set; } = default!; // Pour l'optimistic concurrency

}

public sealed class OutboxDestination
{
    public bool PublishAsNotification { get; init; }        // MediatR internal
    public bool PublishToBroker { get; init; }             // Kafka / Broker

    public string? BrokerTopic { get; init; }              // Topic ou queue
    // public Type? NotificationType { get; init; }          // MediatR Notification type
    public string? PartitionKey { get; init; }            // Broker partition key
    public Dictionary<string, string>? Metadata { get; init; } // Correlation, causation, etc.
}

//var props = new OutboxDestination
//{
//    PublishAsNotification = true,                   // pour MediatR
//    PublishToBroker = true,                         // pour Kafka
//    NotificationType = typeof(OrderCreatedNotification),
//    BrokerTopic = "orders.events",
//    PartitionKey = orderId.ToString(),
//    Metadata = new Dictionary<string, string>
//    {
//        { "CorrelationId", correlationId },
//        { "CausationId", causationId }
//    }
//};