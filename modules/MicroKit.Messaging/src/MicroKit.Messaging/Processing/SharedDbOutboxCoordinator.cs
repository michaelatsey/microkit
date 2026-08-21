namespace MicroKit.Messaging.Processing;

/// <summary>
/// Shared-database <see cref="IOutboxCoordinator"/>. Delegates to
/// <see cref="IOutboxProcessor"/> for cross-tenant batch processing in a single database.
/// </summary>
/// <remarks>
/// The v1 default topology: all tenants share one database and the processor reads
/// <c>TenantId</c> from each <see cref="OutboxMessage"/> row. A per-tenant topology is
/// deferred to <c>MicroKit.Messaging.Multitenancy</c> and will implement
/// <see cref="IOutboxCoordinator"/> without touching this class — which is precisely why
/// the result type is an aggregate rather than a single batch's outcome.
/// </remarks>
internal sealed class SharedDbOutboxCoordinator : IOutboxCoordinator
{
    private readonly IOutboxProcessor _processor;
    private readonly OutboxProcessorOptions _options;

    /// <summary>Initializes a new <see cref="SharedDbOutboxCoordinator"/>.</summary>
    /// <param name="processor">The topology-agnostic batch engine.</param>
    /// <param name="options">Supplies the batch size for the pass.</param>
    public SharedDbOutboxCoordinator(IOutboxProcessor processor, OutboxProcessorOptions options)
    {
        _processor = processor;
        _options = options;
    }

    /// <inheritdoc />
    public ValueTask<OutboxBatchResult> ExecuteAsync(CancellationToken cancellationToken = default)
        => _processor.ProcessBatchAsync(_options.BatchSize, cancellationToken);
}
