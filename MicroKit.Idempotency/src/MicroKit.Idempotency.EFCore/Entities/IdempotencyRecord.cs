using MicroKit.Idempotency.Abstractions.Models;

namespace MicroKit.Idempotency.EFCore.Entities;

public class IdempotencyRecord
{
    public required string Key { get; set; } 
    public required string TenantId { get; set; } 
    public IdempotencyStatus Status { get; set; }
    public string? Response { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? RequestHash { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public byte[]? RowVersion { get; set; }
}
