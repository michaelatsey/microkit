using MicroKit.Messaging.Abstractions.Common;

namespace MicroKit.Messaging.Abstractions.Inbox;

/// <summary>Represents an incoming message stored in the inbox table for at-least-once processing.</summary>
public class InboxMessage
{
    /// <summary>Gets the unique message identifier.</summary>
    public string Id { get; init; } = null!;

    /// <summary>Gets the tenant identifier.</summary>
    public string TenantId { get; init; } = null!;

    /// <summary>Gets the fully-qualified CLR type name of the event payload.</summary>
    public string MessageType { get; init; } = null!;

    /// <summary>Gets the serialized JSON payload.</summary>
    public string Payload { get; init; } = null!;

    /// <summary>Gets the UTC timestamp when the message originally occurred.</summary>
    public DateTimeOffset OccurredOnUtc { get; init; }

    /// <summary>Gets arbitrary transport headers (e.g. Kafka or RabbitMQ headers).</summary>
    public Dictionary<string, string> Headers { get; init; } = new();
}

/// <summary>Tracks per-consumer processing state for an <see cref="InboxMessage"/>.</summary>
public sealed class InboxState
{
    /// <summary>Gets or sets the unique identifier for this state record.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets the tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets or sets the foreign key referencing the parent <see cref="InboxMessage"/>.</summary>
    public string InboxMessageId { get; set; } = null!;

    /// <summary>Gets or sets the name of the consumer responsible for processing this message.</summary>
    public required string ConsumerName { get; set; }

    /// <summary>Gets or sets the current processing status.</summary>
    public MessageStatus Status { get; set; } = MessageStatus.Pending;

    /// <summary>Gets or sets the total number of processing attempts.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Gets or sets the UTC timestamp when this state record was created.</summary>
    public DateTimeOffset OccurredOnUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Gets or sets the UTC timestamp of the last processing attempt.</summary>
    public DateTimeOffset? LastAttemptedAtUtc { get; set; }

    /// <summary>Gets or sets the UTC timestamp of the next scheduled attempt.</summary>
    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    /// <summary>Gets or sets the UTC timestamp until which this state is locked by a processor.</summary>
    public DateTimeOffset? LockedUntilUtc { get; set; }

    /// <summary>Gets or sets the identifier of the worker instance holding the lock.</summary>
    public string? LockedBy { get; set; }

    /// <summary>Gets or sets the UTC timestamp when processing completed successfully.</summary>
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    /// <summary>Gets or sets the last error message recorded during a failed attempt.</summary>
    public string? LastError { get; set; }

    /// <summary>Gets or sets arbitrary local processing metadata.</summary>
    public Dictionary<string, string> ProcessingMetadata { get; set; } = new();

    /// <summary>Gets or sets the optimistic concurrency row version token.</summary>
    public byte[]? RowVersion { get; set; }
}
