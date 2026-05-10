using MicroKit.Messaging.Abstractions.Common;
using System.ComponentModel.DataAnnotations;

namespace MicroKit.Messaging.Abstractions.Inbox;

public class InboxMessage //IMessage
{
    public string Id { get; init; } = null!;
    public string TenantId { get; init; } = null!;
    public string MessageType { get; init; } = null!;
    public string Payload { get; init; } = null!;

    public DateTimeOffset OccurredOnUtc { get; init; }

    // headers arbitraires (Kafka/Rabbit headers)
    public Dictionary<string, string> Headers { get; init; } = new();

    public ICollection<InboxState> InboxStates { get; init; } = [];
    
}

public sealed class InboxState
{
    public Guid Id { get; set; }
    public required string TenantId { get; init; } = null!;

    // Clé étrangère vers InboxMessage
    public string InboxMessageId { get; set; } = null!;
    public InboxMessage Message { get; set; } = null!;

    public required string ConsumerName { get; set; }

    public MessageStatus Status { get; set; } = MessageStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTimeOffset OccurredOnUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastAttemptedAtUtc { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    public DateTimeOffset? LockedUntilUtc { get; set; }
    public string? LockedBy { get; set; } // Ajouté : ID de l'instance du worker
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public string? LastError { get; set; }

    // 👇 metadata de traitement locale
    public Dictionary<string, string> ProcessingMetadata { get; set; } = new();

    public byte[]? RowVersion { get; set; }
}