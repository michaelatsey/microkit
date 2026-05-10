namespace MicroKit.Idempotency.Abstractions.Models;

public record IdempotencyState
{
    public string Key { get; init; }
    public string TenantId { get; init; } 
    public IdempotencyStatus Status { get; init; }

    public string? Response { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public string? RequestHash { get; init; }

    public IdempotencyState(string key, string tenantId, IdempotencyStatus status)
    {
        Key = key;
        TenantId = tenantId;
        Status = status;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }
}
