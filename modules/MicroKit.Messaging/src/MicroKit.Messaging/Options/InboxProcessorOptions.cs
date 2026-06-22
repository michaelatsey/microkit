namespace MicroKit.Messaging.Options;

/// <summary>
/// Configuration options for the inbox background processor.
/// </summary>
public sealed record InboxProcessorOptions
{
    /// <summary>Maximum number of inbox messages to retrieve per poll cycle. Default: 20.</summary>
    public int BatchSize { get; init; } = 20;

    /// <summary>Time to wait between poll cycles when the batch is empty. Default: 5 seconds.</summary>
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Duration for which a processing lease is held. Default: 5 minutes.</summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of handler invocation attempts before a message is dead-lettered.
    /// Default: 10. The back-off formula is <c>2^retryCount</c> seconds, capped at 3600 s.
    /// Symmetric with <see cref="OutboxProcessorOptions.MaxRetries"/>.
    /// </summary>
    public int MaxRetries { get; init; } = 10;
}
