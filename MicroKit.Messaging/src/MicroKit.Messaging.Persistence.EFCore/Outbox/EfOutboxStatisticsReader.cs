using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Outbox;
using Microsoft.EntityFrameworkCore;

namespace MicroKit.Messaging.Persistence.EFCore.Outbox;

public sealed class EfOutboxStatisticsReader<TContext>(TContext context) : IOutboxStatisticsReader
    where TContext : DbContext
{
    public async Task<OutboxStatistics> GetStatisticsAsync(CancellationToken ct = default)
    {
        return await ExecuteStatsQueryAsync(null, ct);
    }

    public async Task<OutboxStatistics> GetStatisticsByTenantAsync(string tenantId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return await ExecuteStatsQueryAsync(tenantId, ct);
    }

    private async Task<OutboxStatistics> ExecuteStatsQueryAsync(string? tenantId, CancellationToken ct)
    {
        var query = context.Set<OutboxMessage>().AsNoTracking();

        if (tenantId != null)
        {
            query = query.Where(m => m.TenantId == tenantId);
        }

        // 1. Récupération des compteurs par statut en une seule passe
        var statsMap = await query
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        // 2. Mesure du Lag (le plus vieux message en attente)
        // L'OrderBy + FirstOrDefault est très rapide grâce à l'index (TenantId, Status, OccurredOnUtc)
        var oldestPending = await query
            .Where(m => m.Status == MessageStatus.Pending)
            .OrderBy(m => m.OccurredOnUtc)
            .Select(m => (DateTimeOffset?)m.OccurredOnUtc)
            .FirstOrDefaultAsync(ct);

        return new OutboxStatistics(
            PendingCount: statsMap.GetValueOrDefault(MessageStatus.Pending),
            ProcessingCount: statsMap.GetValueOrDefault(MessageStatus.Processing),
            PublishedCount: statsMap.GetValueOrDefault(MessageStatus.Published),
            FailedCount: statsMap.GetValueOrDefault(MessageStatus.Failed),
            OldestPendingMessage: oldestPending
        );
    }
}
