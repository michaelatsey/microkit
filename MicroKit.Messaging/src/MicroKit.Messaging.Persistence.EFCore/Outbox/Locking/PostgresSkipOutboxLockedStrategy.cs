using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Outbox;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace MicroKit.Messaging.Persistence.EFCore.Outbox.Locking;

internal sealed class PostgresSkipOutboxLockedStrategy : IOutboxLockingStrategy
{
    public async Task<IReadOnlyList<OutboxMessage>> LockNextAsync(
        DbContext dbContext,
        string tenantId,
        int batchSize,
        TimeSpan lockDuration, // On passe la durée du bail (ex: 5 min)
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var lockUntil = now.Add(lockDuration);

        // 1. Extraction dynamique des métadonnées
        var entityType = dbContext.Model.FindEntityType(typeof(OutboxMessage)) ?? throw new Exception();
        string GetCol(string p) => entityType.FindProperty(p)?.GetColumnName() ?? p;
        string Quote(string i) => $"\"{i}\"";

        var fullName = string.IsNullOrEmpty(entityType.GetSchema())
            ? Quote(entityType.GetTableName()!)
            : $"{Quote(entityType.GetSchema()!)}.{Quote(entityType.GetTableName()!)}";
        // Mapping des colonnes pour éviter tout hardcoding
        var c = new
        {
            Id = GetCol(nameof(OutboxMessage.Id)),
            TenantId = GetCol(nameof(OutboxMessage.TenantId)),
            Status = GetCol(nameof(OutboxMessage.Status)),
            OccurredOn = GetCol(nameof(OutboxMessage.OccurredOnUtc)),
            ScheduledAt = GetCol(nameof(OutboxMessage.ScheduledAtUtc)),
            LockedUntil = GetCol(nameof(OutboxMessage.LockedUntilUtc)),
            LastAttempt = GetCol(nameof(OutboxMessage.LastAttemptedAtUtc)),
            RetryCount = GetCol(nameof(OutboxMessage.RetryCount))
        };


        // 2. SQL avec CTE et FOR UPDATE SKIP LOCKED
        // La CTE sélectionne les IDs à verrouiller, l'UPDATE les marque en 'Processing'
        var sql = $"""
        WITH cte AS (
            SELECT {Quote(c.Id)}
            FROM {fullName}
            WHERE {Quote(c.TenantId)} = @tenantId
              AND (
                  {Quote(c.Status)} = @pending 
                  OR ({Quote(c.Status)} = @processing AND {Quote(c.LockedUntil)} <= @now)
              )
              AND ({Quote(c.ScheduledAt)} IS NULL OR {Quote(c.ScheduledAt)} <= @now)
            ORDER BY {Quote(c.OccurredOn)} ASC
            LIMIT @batchSize
            FOR UPDATE SKIP LOCKED
        )
        UPDATE {fullName} m
        SET {Quote(c.Status)} = @processing,
            {Quote(c.LockedUntil)} = @lockUntil,
            {Quote(c.LastAttempt)} = @now,
            {Quote(c.RetryCount)} = m.{Quote(c.RetryCount)} + 1
        FROM cte
        WHERE m.{Quote(c.Id)} = cte.{Quote(c.Id)}
        RETURNING m.*;
        """;
        var pending = (int)MessageStatus.Pending;
        var processing = (int)MessageStatus.Processing;
        var query = dbContext.Set<OutboxMessage>()
            .FromSqlRaw(sql,
                new NpgsqlParameter("@tenantId", tenantId),
                new NpgsqlParameter("@batchSize", batchSize),
                new NpgsqlParameter("@pending", pending),
                new NpgsqlParameter("@processing", processing),
                new NpgsqlParameter("@now", now),
                new NpgsqlParameter("@lockUntil", lockUntil));

#if DEBUG
        Console.WriteLine($"Executing SqlServerSkipOutboxLockedStrategy query: {query.ToQueryString()}");
#endif
        return await query
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
