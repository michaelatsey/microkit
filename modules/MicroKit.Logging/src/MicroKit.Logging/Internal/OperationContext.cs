namespace MicroKit.Logging.Internal;

/// <summary>
/// Immutable snapshot of correlation and identity data for a single operation scope.
/// All property reads are field reads — zero allocation, ≤ 20 ns.
/// </summary>
internal sealed class OperationContext(
    string correlationId,
    string? traceId,
    string? spanId,
    string? requestId,
    string? operationId,
    string? tenantId,
    string? userId) : IOperationContext
{
    public string CorrelationId { get; } = correlationId;
    public string? TraceId { get; } = traceId;
    public string? SpanId { get; } = spanId;
    public string? RequestId { get; } = requestId;
    public string? OperationId { get; } = operationId;
    public string? TenantId { get; } = tenantId;
    public string? UserId { get; } = userId;
}
