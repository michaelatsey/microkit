using Microsoft.Extensions.Logging;

namespace MicroKit.Logging.Internal;

/// <summary>
/// Implements both <see cref="ILogScopeFactory"/> (sync) and <see cref="IAsyncLogScopeFactory"/> (async).
/// Registered as a single singleton satisfying both interfaces.
/// </summary>
internal sealed class LogScopeFactory(
    ILogger<LogScopeFactory> logger,
    EnrichmentPipeline pipeline,
    MicroKitLoggingOptions options) : ILogScopeFactory, IAsyncLogScopeFactory
{
    private const int DefaultEnrichmentCapacity = 16;

    // ── ILogScopeFactory (sync) ──────────────────────────────────────────────

    public IDisposable BeginOperationScope()
        => CreateScopeCore(GenerateCorrelationId(), operationId: null, tenantId: null, userId: null);

    public IDisposable BeginOperationScope(string correlationId)
    {
        ArgumentException.ThrowIfNullOrEmpty(correlationId);
        return CreateScopeCore(correlationId, operationId: null, tenantId: null, userId: null);
    }

    public IDisposable BeginOperationScope(OperationScopeOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentException.ThrowIfNullOrEmpty(opts.CorrelationId);
        return CreateScopeCore(opts.CorrelationId, opts.OperationId, opts.TenantId, opts.UserId);
    }

    // ── IAsyncLogScopeFactory (async) ────────────────────────────────────────

    public async ValueTask<IDisposable> BeginOperationScopeAsync(CancellationToken ct = default)
        => await CreateScopeCoreAsync(GenerateCorrelationId(), operationId: null, tenantId: null, userId: null, ct).ConfigureAwait(false);

    public async ValueTask<IDisposable> BeginOperationScopeAsync(string correlationId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(correlationId);
        return await CreateScopeCoreAsync(correlationId, operationId: null, tenantId: null, userId: null, ct).ConfigureAwait(false);
    }

    public async ValueTask<IDisposable> BeginOperationScopeAsync(OperationScopeOptions opts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentException.ThrowIfNullOrEmpty(opts.CorrelationId);
        return await CreateScopeCoreAsync(opts.CorrelationId, opts.OperationId, opts.TenantId, opts.UserId, ct).ConfigureAwait(false);
    }

    // ── Core logic ───────────────────────────────────────────────────────────

    private OperationScope CreateScopeCore(
        string correlationId, string? operationId, string? tenantId, string? userId)
    {
        (string? traceId, string? spanId) = options.EnableActivityContextReading
            ? ActivityContextReader.Read()
            : (null, null);

        var enrichmentContext = new LogEnrichmentContext(DefaultEnrichmentCapacity);
        pipeline.Execute(enrichmentContext);

        return FinishScope(correlationId, traceId, spanId, operationId, tenantId, userId, enrichmentContext);
    }

    private async ValueTask<IDisposable> CreateScopeCoreAsync(
        string correlationId, string? operationId, string? tenantId, string? userId, CancellationToken ct)
    {
        (string? traceId, string? spanId) = options.EnableActivityContextReading
            ? ActivityContextReader.Read()
            : (null, null);

        var enrichmentContext = new LogEnrichmentContext(DefaultEnrichmentCapacity);
        await pipeline.ExecuteAsync(enrichmentContext, ct).ConfigureAwait(false);

        return FinishScope(correlationId, traceId, spanId, operationId, tenantId, userId, enrichmentContext);
    }

    private OperationScope FinishScope(
        string correlationId, string? traceId, string? spanId,
        string? operationId, string? tenantId, string? userId,
        LogEnrichmentContext enrichmentContext)
    {
        var context = new OperationContext(
            correlationId, traceId, spanId,
            requestId: null, operationId, tenantId, userId);

        var scopeState = BuildScopeState(context, enrichmentContext);
        var previousContext = LogContextAccessor.CurrentContext;

        LogContextAccessor.CurrentContext = context;

        var melScope = logger.BeginScope(scopeState);
        logger.OperationScopeCreated(correlationId, operationId);

        return new OperationScope(previousContext, melScope, logger);
    }

    private string GenerateCorrelationId()
        => options.CorrelationIdFactory?.Invoke() ?? CorrelationIdGenerator.Generate();

    private static KeyValuePair<string, object?>[] BuildScopeState(
        OperationContext context, LogEnrichmentContext enrichmentContext)
    {
        var enricherProps = enrichmentContext.GetProperties();

        // Count non-null canonical properties
        int fixedCount = 1; // CorrelationId always present
        if (context.TraceId is not null) fixedCount++;
        if (context.SpanId is not null) fixedCount++;
        if (context.RequestId is not null) fixedCount++;
        if (context.OperationId is not null) fixedCount++;
        if (context.TenantId is not null) fixedCount++;
        if (context.UserId is not null) fixedCount++;

        var state = new KeyValuePair<string, object?>[fixedCount + enricherProps.Length];
        int i = 0;

        state[i++] = new(LogPropertyNames.CorrelationId, context.CorrelationId);
        if (context.TraceId is not null)    state[i++] = new(LogPropertyNames.TraceId, context.TraceId);
        if (context.SpanId is not null)     state[i++] = new(LogPropertyNames.SpanId, context.SpanId);
        if (context.RequestId is not null)  state[i++] = new(LogPropertyNames.RequestId, context.RequestId);
        if (context.OperationId is not null) state[i++] = new(LogPropertyNames.OperationId, context.OperationId);
        if (context.TenantId is not null)   state[i++] = new(LogPropertyNames.TenantId, context.TenantId);
        if (context.UserId is not null)     state[i++] = new(LogPropertyNames.UserId, context.UserId);

        foreach (var prop in enricherProps)
            state[i++] = prop;

        return state;
    }
}
