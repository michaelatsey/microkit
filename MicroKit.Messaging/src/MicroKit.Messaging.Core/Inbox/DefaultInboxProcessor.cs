using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Inbox;
using MicroKit.Messaging.Abstractions.Persistence;
using MicroKit.Messaging.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MicroKit.Messaging.Core.Inbox;

/// <summary>
/// Default inbox processor that fetches pending <see cref="InboxState"/> records and publishes them
/// to the configured <see cref="IInboxPublisher"/>, applying exponential back-off on failure.
/// </summary>
public class DefaultInboxProcessor : IInboxProcessor
{
    private readonly IInboxStateFetcher _fetcher;
    private readonly IInboxStateRepository _repository;
    private readonly IInboxMessageRepository _messageRepository;
    private readonly IMessagingUnitOfWork _unitOfWork;
    private readonly IInboxPublisher _publisher;
    private readonly InboxOptions _options;
    private readonly ILogger<DefaultInboxProcessor> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="DefaultInboxProcessor"/>.
    /// </summary>
    public DefaultInboxProcessor(
        IInboxStateRepository repository,
        IInboxMessageRepository messageRepository,
        IOptions<InboxOptions> options,
        ILogger<DefaultInboxProcessor> logger,
        IInboxStateFetcher fetcher,
        IInboxPublisher publisher,
        IMessagingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _messageRepository = messageRepository;
        _options = options.Value;
        _logger = logger;
        _fetcher = fetcher;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Processes a batch of pending inbox states for the given tenant and consumer.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="consumerName">The consumer name.</param>
    /// <param name="batchSize">Maximum number of states to process.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task ProcessBatchAsync(
        string tenantId,
        string consumerName,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var states = await _fetcher.FetchNextBatchAsync(
            tenantId,
            consumerName,
            batchSize,
            TimeSpan.FromMinutes(5),
            cancellationToken);

        if (!states.Any())
        {
            _logger.LogTrace("No pending inbox states found for tenant {TenantId} / consumer {ConsumerName}.", tenantId, consumerName);
            return;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("[Tenant:{TenantId}] Processing batch of {Count} inbox states.", tenantId, states.Count);
        }

        foreach (var state in states)
        {
            await ProcessStateInternalAsync(state, cancellationToken);
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[Tenant:{TenantId}] Successfully committed batch of {Count} inbox states.", tenantId, states.Count);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "[Tenant:{TenantId}] Failed to commit inbox batch. States will be retried when their lease expires.", tenantId);
            throw;
        }
    }

    private async Task ProcessStateInternalAsync(InboxState state, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _messageRepository.GetByIdAsync(state.InboxMessageId, cancellationToken)
                ?? throw new InvalidOperationException($"InboxMessage '{state.InboxMessageId}' not found for InboxState '{state.Id}'.");

            var context = new InboxContext(
                MessageId: state.InboxMessageId,
                MessageType: message.MessageType,
                Payload: message.Payload,
                Headers: message.Headers);

            await _publisher.PublishAsync(context, cancellationToken);

            state.Status = MessageStatus.Published;
            state.ProcessedAtUtc = DateTimeOffset.UtcNow;
            state.LockedUntilUtc = null;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Inbox state {StateId} for message {MessageId} published successfully.", state.Id, state.InboxMessageId);
            }
        }
        catch (Exception ex)
        {
            await HandleFailureInternalAsync(state, ex, cancellationToken);
        }
    }

    private Task HandleFailureInternalAsync(InboxState state, Exception exception, CancellationToken cancellationToken)
    {
        state.LastError = exception.Message;

        if (state.AttemptCount >= _options.MaxProcessingAttempts)
        {
            state.Status = MessageStatus.Failed;
            _logger.LogError(exception,
                "Inbox state {StateId} reached max retries ({MaxCount}) and is marked as Failed.",
                state.Id, state.AttemptCount);
        }
        else
        {
            state.Status = MessageStatus.Pending;
            var delayMinutes = Math.Pow(2, state.AttemptCount);
            state.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddMinutes(delayMinutes);
            state.LockedUntilUtc = null;
            _logger.LogWarning(
                "Inbox state {StateId} failed. Attempt {Count}/{Max}. Next retry in {Delay}min. Error: {Error}",
                state.Id, state.AttemptCount, _options.MaxProcessingAttempts, delayMinutes, exception.Message);
        }

        return Task.CompletedTask;
    }
}
