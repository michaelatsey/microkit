namespace MicroKit.Messaging.Execution;

/// <summary>
/// Default <see cref="IExecutionContext"/> implementation — a simple property bag
/// populated by the outbox and inbox processors from the current message row.
/// </summary>
internal sealed class ExecutionContext : IExecutionContext
{
    /// <inheritdoc />
    public string? TenantId { get; init; }

    /// <inheritdoc />
    public string? CorrelationId { get; init; }

    /// <inheritdoc />
    public string? CausationId { get; init; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Properties { get; init; }
        = new Dictionary<string, object?>();
}
