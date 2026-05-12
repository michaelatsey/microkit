using MicroKit.Idempotency.Abstractions.Contracts;
using MicroKit.Idempotency.Abstractions.Models;
using MicroKit.Idempotency.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MicroKit.Idempotency.Core;

/// <summary>Background worker that periodically purges expired idempotency records from the store.</summary>
public class IdempotencyCleanupWorker: BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IdempotencyOptions _options;
    private readonly ILogger<IdempotencyCleanupWorker> _logger;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="scopeFactory">Factory for creating DI scopes during cleanup.</param>
    /// <param name="options">Idempotency configuration.</param>
    /// <param name="logger">Logger.</param>
    public IdempotencyCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<IdempotencyOptions> options,
        ILogger<IdempotencyCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CleanupRunInterval.HasValue)
        {
            _logger.LogInformation("Idempotency cleanup is disabled.");
            return;
        }

        _logger.LogInformation("Idempotency Cleanup Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DoCleanupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during outbox cleanup cycle.");
            }

            // Utilisation de l'intervalle dédié au nettoyage
            await Task.Delay(_options.CleanupRunInterval.Value, stoppingToken);
        }
    }

    private async Task DoCleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var cleanupService = scope.ServiceProvider.GetRequiredService<IIdempotencyCleanupService>();

        _logger.LogDebug("Starting outbox cleanup cycle.");

        // --- 1. NETTOYAGE DES MESSAGES PUBLIÉS ---
        // On garde les succès pendant une période courte (ex: 7 jours)
        var publishedCutoff = DateTimeOffset.UtcNow.Subtract(_options.RetentionPeriod);

        int deletedPublished = await ExecuteDeletionLoop(
            cleanupService,
            publishedCutoff,
            IdempotencyStatus.Completed,
            ct);

        if (deletedPublished > 0 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Cleaned up {Count} published outbox messages.", deletedPublished);
        }

        // --- 2. NETTOYAGE DES ÉCHECS ---
        // On garde les échecs plus longtemps pour analyse (ex: 30 jours)
        var failedCutoff = DateTimeOffset.UtcNow.Subtract(_options.FailedRetentionPeriod);
        int deletedFailed = await ExecuteDeletionLoop(
            cleanupService,
            failedCutoff,
            IdempotencyStatus.Failed,
            ct);

        if (deletedFailed > 0 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Cleaned up {Count} failed outbox messages.", deletedFailed);
        }
    }

    /// <summary>
    /// Exécute la suppression par batch jusqu'à ce qu'il n'y ait plus rien à supprimer pour le critère donné.
    /// </summary>
    private async Task<int> ExecuteDeletionLoop(
        IIdempotencyCleanupService service,
        DateTimeOffset cutoff,
        IdempotencyStatus status,
        CancellationToken ct)
    {
        int totalDeleted = 0;
        int deletedInBatch;

        do
        {
            deletedInBatch = await service.CleanupAsync(
                cutoff,
                status,
                _options.CleanupBatchSize,
                ct);

            totalDeleted += deletedInBatch;

            // Si on a atteint la taille du batch, c'est qu'il en reste probablement d'autres
        } while (deletedInBatch >= _options.CleanupBatchSize && !ct.IsCancellationRequested);

        return totalDeleted;
    }
}
