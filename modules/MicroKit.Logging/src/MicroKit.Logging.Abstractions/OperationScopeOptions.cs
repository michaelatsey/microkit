namespace MicroKit.Logging;

/// <summary>
/// Options for creating a structured log scope with full operation context initialization.
/// Adding new optional properties to this record is a non-breaking change.
/// </summary>
public sealed record OperationScopeOptions
{
    /// <summary>
    /// The cross-service correlation identifier. Must not be null or empty.
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    /// Optional logical operation identifier (e.g., a CQRS command name).
    /// </summary>
    public string? OperationId { get; init; }

    /// <summary>
    /// Optional multi-tenant identifier.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Optional authenticated user identifier.
    /// </summary>
    public string? UserId { get; init; }
}
