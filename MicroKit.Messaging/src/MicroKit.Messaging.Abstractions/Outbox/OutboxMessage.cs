using MicroKit.Messaging.Abstractions.Common;

namespace MicroKit.Messaging.Abstractions.Outbox;

/// <summary>
/// Represents a single entry in the outbox table, storing a serialized event envelope
/// ready for reliable at-least-once delivery.
/// </summary>
public class OutboxMessage : IMessage
{
    /// <summary>Gets or sets the unique message identifier.</summary>
    public string Id { get; set; } = null!;

    /// <summary>Gets or sets the tenant identifier.</summary>
    public string TenantId { get; set; } = null!;

    /// <summary>Gets or sets the fully-qualified CLR type name of the event payload.</summary>
    public string MessageType { get; set; } = null!;

    /// <summary>Gets or sets the serialized JSON envelope.</summary>
    public string Payload { get; set; } = null!;

    /// <summary>Gets or sets the correlation identifier for distributed tracing.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Gets or sets the causation identifier.</summary>
    public string? CausationId { get; set; }

    /// <summary>Gets or sets an optional idempotency key for deduplication.</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Gets or sets a value indicating whether this message should be published as a MediatR notification.</summary>
    public bool PublishAsNotification { get; set; }

    /// <summary>Gets or sets a value indicating whether this message should be forwarded to the message broker.</summary>
    public bool PublishToBroker { get; set; }

    /// <summary>Gets or sets the broker topic or queue name.</summary>
    public string? BrokerTopic { get; set; }

    /// <summary>Gets or sets the broker partition key.</summary>
    public string? PartitionKey { get; set; }

    /// <summary>Gets or sets optional destination metadata stored as a raw JSON string.</summary>
    public string? Metadata { get; set; }

    /// <summary>Gets or sets the UTC timestamp when the message was enqueued.</summary>
    public DateTimeOffset OccurredOnUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Gets or sets the UTC timestamp when the message was successfully processed.</summary>
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    /// <summary>Gets or sets the UTC timestamp before which the message should not be retried.</summary>
    public DateTimeOffset? ScheduledAtUtc { get; set; }

    /// <summary>Gets or sets the UTC timestamp until which this message is locked by a processor.</summary>
    public DateTimeOffset? LockedUntilUtc { get; set; }

    /// <summary>Gets or sets the UTC timestamp of the last processing attempt.</summary>
    public DateTimeOffset? LastAttemptedAtUtc { get; set; }

    /// <summary>Gets or sets the current processing status.</summary>
    public MessageStatus Status { get; set; } = MessageStatus.Pending;

    /// <summary>Gets or sets the number of delivery attempts made so far.</summary>
    public int RetryCount { get; set; }

    /// <summary>Gets or sets the last error message recorded during a failed attempt.</summary>
    public string? Error { get; set; }

    /// <summary>Gets or sets the optimistic concurrency row version token.</summary>
    public byte[] RowVersion { get; set; } = default!;
}

/// <summary>Describes the delivery destination options for an outbox message.</summary>
public sealed class OutboxDestination
{
    /// <summary>Gets a value indicating whether to publish the payload as a MediatR notification.</summary>
    public bool PublishAsNotification { get; init; }

    /// <summary>Gets a value indicating whether to forward the payload to the message broker.</summary>
    public bool PublishToBroker { get; init; }

    /// <summary>Gets the broker topic or queue name.</summary>
    public string? BrokerTopic { get; init; }

    /// <summary>Gets the broker partition key.</summary>
    public string? PartitionKey { get; init; }

    /// <summary>Gets optional key-value metadata for the destination.</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
