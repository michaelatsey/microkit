namespace MicroKit.Idempotency.Abstractions.Models;

/// <summary>Captures the persisted state of an idempotency record across its full lifecycle.</summary>
public record IdempotencyState
{
    /// <summary>Gets the idempotency key that uniquely identifies the operation.</summary>
    public string Key { get; init; }

    /// <summary>Gets the tenant identifier associated with this record.</summary>
    public string TenantId { get; init; }

    /// <summary>Gets the current lifecycle status of the idempotency record.</summary>
    public IdempotencyStatus Status { get; init; }

    /// <summary>Gets the serialized response cached after the operation completed, or <see langword="null"/> if not yet complete.</summary>
    public string? Response { get; init; }

    /// <summary>Gets the UTC timestamp when this record was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Gets the UTC timestamp when this record was completed, or <see langword="null"/> if still in progress.</summary>
    public DateTimeOffset? CompletedAtUtc { get; init; }

    /// <summary>Gets a deterministic hash of the original request, used to detect payload drift across duplicate calls.</summary>
    public string? RequestHash { get; init; }

    /// <summary>Initializes a new <see cref="IdempotencyState"/> in the <see cref="IdempotencyStatus.Processing"/> lifecycle.</summary>
    /// <param name="key">The idempotency key.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="status">The initial status.</param>
    public IdempotencyState(string key, string tenantId, IdempotencyStatus status)
    {
        Key = key;
        TenantId = tenantId;
        Status = status;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }
}
