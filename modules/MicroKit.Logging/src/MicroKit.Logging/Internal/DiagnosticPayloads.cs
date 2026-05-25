namespace MicroKit.Logging.Internal;

internal readonly struct EnrichmentExecutedPayload(int enricherCount, string operationId, double elapsedMs)
{
    public int EnricherCount { get; } = enricherCount;
    public string OperationId { get; } = operationId;
    public double ElapsedMs { get; } = elapsedMs;
}

internal readonly struct EnrichmentFaultedPayload(string enricherType, Exception exception, string operationId)
{
    public string EnricherType { get; } = enricherType;
    public Exception Exception { get; } = exception;
    public string OperationId { get; } = operationId;
}

internal readonly struct ScopeCreatedPayload(string scopeName, string operationId, string correlationId)
{
    public string ScopeName { get; } = scopeName;
    public string OperationId { get; } = operationId;
    public string CorrelationId { get; } = correlationId;
}

internal readonly struct ScopeDisposedPayload(string scopeName, string operationId, double durationMs)
{
    public string ScopeName { get; } = scopeName;
    public string OperationId { get; } = operationId;
    public double DurationMs { get; } = durationMs;
}

internal readonly struct CorrelationGeneratedPayload(string correlationId)
{
    public string CorrelationId { get; } = correlationId;
}

internal readonly struct CorrelationResolvedPayload(string correlationId, string source)
{
    public string CorrelationId { get; } = correlationId;
    public string Source { get; } = source;
}
