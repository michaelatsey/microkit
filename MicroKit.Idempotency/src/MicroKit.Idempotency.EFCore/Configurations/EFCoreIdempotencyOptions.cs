namespace MicroKit.Idempotency.EFCore.Configurations;

public class EFCoreIdempotencyOptions
{
    /// <summary>
    /// Gets or sets the default time-to-live for idempotency entries
    /// </summary>
    public TimeSpan? DefaultTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets whether to renew expiration when completing an operation
    /// </summary>
    public bool RenewExpirationOnComplete { get; set; } = false;

    /// <summary>
    /// Gets or sets the schema name for the idempotency table
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>
    /// Gets or sets the table name for idempotency entries
    /// </summary>
    public string TableName { get; set; } = "IdempotencyRecords";

    /// <summary>
    /// Gets or sets whether to enable automatic cleanup of expired entries
    /// </summary>
    public bool EnableAutoCleanup { get; set; } = false;

    /// <summary>
    /// Gets or sets the cleanup interval for expired entries
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}
