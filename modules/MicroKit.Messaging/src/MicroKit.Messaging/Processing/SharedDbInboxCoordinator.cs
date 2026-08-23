namespace MicroKit.Messaging.Processing;

/// <summary>
/// Shared-database <see cref="IInboxCoordinator"/>. Delegates to <see cref="IInboxProcessor"/>
/// for cross-tenant batch processing in a single database.
/// </summary>
/// <remarks>
/// The v1 default topology (ADR-MSG-002). All tenants share one database and the processor reads
/// <c>TenantId</c> from each row. A per-tenant topology is deferred to
/// <c>MicroKit.Messaging.Multitenancy</c> and will implement <see cref="IInboxCoordinator"/>
/// without touching this class — which is why the result type is an aggregate rather than a
/// single batch's outcome.
/// </remarks>
internal sealed class SharedDbInboxCoordinator : IInboxCoordinator
{
    private readonly IInboxProcessor _processor;
    private readonly InboxProcessorOptions _options;

    /// <summary>Initializes a new <see cref="SharedDbInboxCoordinator"/>.</summary>
    /// <param name="processor">The batch engine.</param>
    /// <param name="options">Supplies the batch size.</param>
    public SharedDbInboxCoordinator(IInboxProcessor processor, InboxProcessorOptions options)
    {
        _processor = processor;
        _options = options;
    }

    /// <inheritdoc />
    public ValueTask<InboxBatchResult> ExecuteAsync(CancellationToken cancellationToken = default)
        => _processor.ProcessBatchAsync(_options.BatchSize, cancellationToken);
}
