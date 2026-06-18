namespace MicroKit.Messaging.Processing;

/// <summary>
/// Shared-database <see cref="IInboxCoordinator"/> implementation. Delegates to
/// <see cref="IInboxProcessor"/> for cross-tenant batch processing in a single database.
/// </summary>
/// <remarks>
/// This is the v1 default coordinator topology, symmetric with
/// <see cref="SharedDbOutboxCoordinator"/>. A per-tenant topology is deferred to
/// <c>MicroKit.Messaging.Multitenancy</c> and will implement <see cref="IInboxCoordinator"/>
/// without modifying this class.
/// </remarks>
internal sealed class SharedDbInboxCoordinator : IInboxCoordinator
{
    private readonly IInboxProcessor _processor;
    private readonly InboxProcessorOptions _options;

    /// <summary>
    /// Initializes a new <see cref="SharedDbInboxCoordinator"/>.
    /// </summary>
    public SharedDbInboxCoordinator(IInboxProcessor processor, InboxProcessorOptions options)
    {
        _processor = processor;
        _options = options;
    }

    /// <inheritdoc />
    public Task ExecuteAsync(CancellationToken cancellationToken = default)
        => _processor.ProcessBatchAsync(_options.BatchSize, cancellationToken);
}
