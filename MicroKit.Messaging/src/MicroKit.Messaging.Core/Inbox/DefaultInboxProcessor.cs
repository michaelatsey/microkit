using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Inbox;
using MicroKit.Messaging.Abstractions.Persistence;
using MicroKit.Messaging.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MicroKit.Messaging.Core.Inbox;

public class DefaultInboxProcessor : IInboxProcessor
{
    private readonly IInboxStateFetcher _fetcher;
    private readonly IInboxStateRepository _repository;
    private readonly IMessagingUnitOfWork _unitOfWork;
    //private readonly IMessageSerializer _serializer;
    private readonly IInboxPublisher _publisher;
    private readonly InboxOptions _options;
    private readonly ILogger<DefaultInboxProcessor> _logger;
    private const string ConsumerGroup = "publisher";
    public DefaultInboxProcessor(
        IInboxStateRepository repository,
        //IMessageSerializer serializer,
        IOptions<InboxOptions> options,
        ILogger<DefaultInboxProcessor> logger,
        IInboxStateFetcher fetcher,
        IInboxPublisher publisher,
        IMessagingUnitOfWork unitOfWork)
    {
        _repository = repository;
        //_serializer = serializer;
        _options = options.Value;
        _logger = logger;
        _fetcher = fetcher;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
    }
    /// <summary>
    /// Processes the batch asynchronous.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="consumerName">Name of the consumer.</param>
    /// <param name="batchSize">Size of the batch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task ProcessBatchAsync(
        string tenantId,
        string consumerName, 
        int batchSize, 
        CancellationToken cancellationToken = default)
    {
        var messages = await _fetcher.FetchNextBatchAsync(
            tenantId,
            consumerName,
            batchSize,
            TimeSpan.FromMinutes(5),
            cancellationToken);

        if (!messages.Any())
        {
            _logger.LogTrace("No pending messages found in outbox.");
            return;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Processing batch of {Count} outbox messages.", messages.Count);
        }

        foreach (var message in messages)
        {
            await ProcessMessageInternalAsync(message, cancellationToken);
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Successfully processed and committed batch of {Count} messages.", messages.Count);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to commit outbox batch. Database consistency might be at risk.");
            // Note: Si le commit échoue ici, les messages restent en statut 'Processing' 
            // ou 'Pending' selon ta stratégie de lock, et seront re-tentés au prochain cycle.
        }
    }

    /// <summary>
    /// Processes the state asynchronous.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task ProcessMessageInternalAsync(InboxState state,CancellationToken cancellationToken = default)
    {
        try
        {
            // Mark as processing
            // ATTENTION : Ici, plus besoin de faire state.Status = Processing 
            // et UpdateAsync au début, car la stratégie SQL l'a DÉJÀ FAIT !

            var context = new InboxContext(
                MessageId: state.InboxMessageId,
                MessageType: state.Message.MessageType,
                Payload: state.Message.Payload,
                Headers: state.Message.Headers // Le correlationId, l' idempotencyKey et autres...
            );


            // Publication  (Mediart par exemple)

            await _publisher.PublishAsync(context,cancellationToken);

            // Mark as published
            state.Status = MessageStatus.Published;
            state.ProcessedAtUtc = DateTimeOffset.UtcNow;
            state.LockedUntilUtc = null;

            // await _repository.UpdateAsync(state, cancellationToken);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Message {MessageId} sent to transport successfully",
                    state.Id);
            }
        }
        catch (Exception ex)
        {
            await HandleFailureInternalAsync(state, ex, cancellationToken);
        }
    }

    /// <summary>
    /// Handles the failure asynchronous.
    /// </summary>
    /// <param name="inboxState">The state.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task HandleFailureInternalAsync(
        InboxState inboxState,
        Exception exception,
        CancellationToken cancellationToken)
    {
        inboxState.LastError = exception.Message;

        if (inboxState.AttemptCount >= _options.MaxProcessingAttempts)
        {
            // Échec définitif : On arrête de s'acharner
            inboxState.Status = MessageStatus.Failed;
            _logger.LogError(exception,
                "Inbox state {MessageId} reached max retries ({MaxCount}) and is marked as Failed.",
                inboxState.Id, inboxState.AttemptCount);
        }
        else
        {
            inboxState.Status = MessageStatus.Pending;

            // Calcul du délai exponentiel : 2, 4, 8, 16... minutes
            var delayMinutes = Math.Pow(2, inboxState.AttemptCount);
            inboxState.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddMinutes(delayMinutes);
            _logger.LogWarning("Failed to publish inbox state {MessageId}. Attempt {Count}/{Max}. Error: {Error}",
                inboxState.Id, inboxState.AttemptCount, _options.MaxProcessingAttempts, exception.Message);
        }
    }
}

