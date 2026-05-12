using MicroKit.Idempotency.Abstractions.Models;

namespace MicroKit.Idempotency.EFCore.Entities;

/// <summary>EF Core entity that persists idempotency state for a unique key/tenant pair.</summary>
public class IdempotencyRecord
{
    /// <summary>Gets or sets the idempotency key.</summary>
    public required string Key { get; set; }
    /// <summary>Gets or sets the tenant identifier that owns this record.</summary>
    public required string TenantId { get; set; }
    /// <summary>Gets or sets the current processing status.</summary>
    public IdempotencyStatus Status { get; set; }
    /// <summary>Gets or sets the serialized response payload, if completed.</summary>
    public string? Response { get; set; }
    /// <summary>Gets or sets the UTC timestamp when this record was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
    /// <summary>Gets or sets the UTC timestamp when processing completed, if applicable.</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }
    /// <summary>Gets or sets the hash of the original request body, used for conflict detection.</summary>
    public string? RequestHash { get; set; }
    /// <summary>Gets or sets the UTC timestamp after which this record may be purged.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    /// <summary>Gets or sets the concurrency token for optimistic locking.</summary>
    public byte[]? RowVersion { get; set; }
}
