using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Outbox;
using MicroKit.Messaging.Abstractions.Persistence;
using MicroKit.Messaging.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MicroKit.Messaging.Core.Outbox;

/// <summary>
/// Réprésente le processeur de la boîte d'envoi (Outbox) par défaut du système de messagerie, responsable de traiter les messages en attente dans la boîte d'envoi, de les envoyer via le transport configuré et de gérer les états des messages en fonction du succès ou de l'échec de l'envoi, en respectant les options de configuration définies pour la boîte d'envoi.
/// </summary>
/// <seealso cref="MicroKit.Messaging.Abstractions.Outbox.IOutboxProcessor" />
public class DefaultOutboxProcessor : IOutboxProcessor
{
    private readonly IOutboxMessageFetcher _fetcher;
    /// <summary>
    /// The repository
    /// </summary>
    private readonly IOutboxRepository _repository;
    /// <summary>
    /// The publisher
    /// </summary>
    private readonly IOutboxPublisher _publisher;

    private readonly IMessagingUnitOfWork _unitOfWork;
    //private readonly IMessageTransport _transport;
    /// <summary>
    /// The logger
    /// </summary>
    private readonly ILogger<DefaultOutboxProcessor> _logger;
    /// <summary>
    /// The options
    /// </summary>
    private readonly OutboxOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultOutboxProcessor"/> class.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="publisher">The publisher.</param>
    /// <param name="options">The options.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="fetcher">The fetcher.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public DefaultOutboxProcessor(
        IOutboxRepository repository,
        IOutboxPublisher publisher,
        IOptions<OutboxOptions> options,
        ILogger<DefaultOutboxProcessor> logger,
        IOutboxMessageFetcher fetcher,
        IMessagingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
        _fetcher = fetcher;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Processes the batch asynchronous.
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="batchSize">Size of the batch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task ProcessBatchAsync(
        string tenantId,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var lockDuration = _options.LockDurationInMinutes;
        var messages = await _fetcher.FetchNextBatchAsync(tenantId, batchSize, lockDuration, cancellationToken);

        if (!messages.Any())
        {
            _logger.LogTrace("No pending messages found in outbox.");
            return;
        }

        if(_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("[Tenant:{TenantId}] Processing batch of {Count} messages.", tenantId, messages.Count);
        }

        foreach (var message in messages)
        {
            await ProcessMessageInternalAsync(message, cancellationToken);
            await _repository.UpdateAsync(message, cancellationToken);
        }

        // UNE SEULE transaction pour valider tout le batch en base de données
        // C'est ici que l'efficacité du Unit of Work prend tout son sens.
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            if(_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Successfully processed and committed batch of {Count} messages.", messages.Count);
            }
        }
        catch (Exception ex)
        {
            // Messages remain in 'Processing' status until their lock expires and will be
            // retried on the next cycle. The exception is rethrown so the worker can back off.
            if (_logger.IsEnabled(LogLevel.Critical))
                _logger.LogCritical(ex, "[Tenant:{TenantId}] Failed to commit batch. Messages will be retried when their lease expires.", tenantId);

            throw;
        }
    }

    /// <summary>
    /// Processes the message asynchronous.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task ProcessMessageInternalAsync(
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            // Mark as processing
            // ATTENTION : Ici, plus besoin de faire message.Status = Processing 
            // et UpdateAsync au début, car la stratégie SQL l'a DÉJÀ FAIT !
            message.RetryCount++;

            await _publisher.PublishAsync(message, cancellationToken);

            // Mark as published
            message.Status = MessageStatus.Published;
            message.ProcessedAtUtc = DateTimeOffset.UtcNow;
            message.Error = null;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Message {MessageId} sent to transport successfully",
                    message.Id);
            }
        }
        catch (Exception ex)
        {
            await HandleFailureInternalAsync(message, ex, cancellationToken);
        }
    }

    /// <summary>
    /// Handles the failure asynchronous.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private Task HandleFailureInternalAsync(
        OutboxMessage message,
        Exception exception,
        CancellationToken cancellationToken)
    {
        message.Error = exception.Message;

        if (message.RetryCount >= _options.MaxRetryCount)
        {
            message.Status = MessageStatus.Failed;
            _logger.LogError(exception,
                "Outbox Message {MessageId} reached max retries ({MaxCount}) and is marked as Failed.",
                message.Id, message.RetryCount);
        }
        else
        {
            message.Status = MessageStatus.Pending;
            // Calcul du délai exponentiel : 2, 4, 8, 16... minutes
            var delaySeconds = Math.Min(3600, Math.Pow(2, message.RetryCount));// Max 1h
            message.ScheduledAtUtc = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);

            // On libère le verrou pour qu'il redevienne éligible après le délai
            message.LockedUntilUtc = null;

            _logger.LogWarning("Retrying message {Id} in {Delay}s. (Attempt {Count})",
                message.Id, delaySeconds, message.RetryCount);
        }
        return Task.CompletedTask;
    }
}
