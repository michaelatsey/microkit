namespace MicroKit.Messaging.Processing;

/// <summary>
/// Shared-database <see cref="IOutboxCoordinator"/> implementation. Delegates to
/// <see cref="IOutboxProcessor"/> for cross-tenant batch processing in a single database.
/// </summary>
/// <remarks>
/// This is the v1 default coordinator topology. All tenants share one database; the
/// processor reads <c>TenantId</c> from each <see cref="OutboxMessage"/> row. A per-tenant
/// topology (separate databases) is deferred to <c>MicroKit.Messaging.Multitenancy</c>
/// and will implement <see cref="IOutboxCoordinator"/> without modifying this class.
/// </remarks>
internal sealed class SharedDbOutboxCoordinator : IOutboxCoordinator
{
    private readonly IOutboxProcessor _processor;
    private readonly OutboxProcessorOptions _options;

    /// <summary>
    /// Initializes a new <see cref="SharedDbOutboxCoordinator"/>.
    /// </summary>
    public SharedDbOutboxCoordinator(IOutboxProcessor processor, OutboxProcessorOptions options)
    {
        _processor = processor;
        _options = options;
    }

    /// <inheritdoc />
    public Task ExecuteAsync(CancellationToken cancellationToken = default)
        => _processor.ProcessBatchAsync(_options.BatchSize, cancellationToken);
}
