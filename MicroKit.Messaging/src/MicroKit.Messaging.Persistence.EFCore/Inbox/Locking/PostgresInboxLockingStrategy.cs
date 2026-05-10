using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace MicroKit.Messaging.Persistence.EFCore.Inbox.Locking;

internal sealed class PostgresInboxLockingStrategy : IInboxLockingStrategy
{

    public async Task<IReadOnlyList<InboxState>> LockNextAsync(
        DbContext dbContext,
        string tenantId,
        string consumerName,
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var lockUntil = now.Add(lockDuration);

        // 1. Extraction des métadonnées EF Core
        var stateEntityType = dbContext.Model.FindEntityType(typeof(InboxState))
            ?? throw new InvalidOperationException("InboxState non configuré.");
        var messageEntityType = dbContext.Model.FindEntityType(typeof(InboxMessage))
            ?? throw new InvalidOperationException("InboxMessage non configuré.");

        string GetCol(IEntityType et, string prop) => et.FindProperty(prop)?.GetColumnName() ?? prop;
        string Quote(string ident) => $"\"{ident}\"";

        var sTable = stateEntityType.GetTableName();
        var sSchema = stateEntityType.GetSchema();
        var mTable = messageEntityType.GetTableName();
        var mSchema = messageEntityType.GetSchema();

        var fullStateName = string.IsNullOrEmpty(sSchema) ? Quote(sTable!) : $"{Quote(sSchema)}.{Quote(sTable!)}";
        var fullMsgName = string.IsNullOrEmpty(mSchema) ? Quote(mTable!) : $"{Quote(mSchema)}.{Quote(mTable!)}";

        // Mapping colonnes
        var sc = new
        {
            Id = GetCol(stateEntityType, nameof(InboxState.Id)),
            Status = GetCol(stateEntityType, nameof(InboxState.Status)),
            LockedUntil = GetCol(stateEntityType, nameof(InboxState.LockedUntilUtc)),
            LastAttempt = GetCol(stateEntityType, nameof(InboxState.LastAttemptedAtUtc)),
            AttemptCount = GetCol(stateEntityType, nameof(InboxState.AttemptCount)),
            ConsumerName = GetCol(stateEntityType, nameof(InboxState.ConsumerName)),
            NextAttempt = GetCol(stateEntityType, nameof(InboxState.NextAttemptAtUtc)),
            TenantId = GetCol(stateEntityType, nameof(InboxState.TenantId)),
            MsgFk = GetCol(stateEntityType, nameof(InboxState.InboxMessageId))
        };

        var mc = new
        {
            Id = GetCol(messageEntityType, nameof(InboxMessage.Id)),
            OccurredOn = GetCol(messageEntityType, nameof(InboxMessage.OccurredOnUtc))
        };

        // 2. SQL Postgres avec CTE (Common Table Expression) et FOR UPDATE SKIP LOCKED
        // On joint le message dès le départ pour trier par OccurredOnUtc
        var sql = $"""
        WITH cte AS (
            SELECT s.{Quote(sc.Id)}
            FROM {fullStateName} s
            INNER JOIN {fullMsgName} m ON s.{Quote(sc.MsgFk)} = m.{Quote(mc.Id)}
            WHERE s.{Quote(sc.TenantId)} = @tenantId
              AND s.{Quote(sc.ConsumerName)} = @consumerName
              AND s.{Quote(sc.Status)} = @pending
              AND (s.{Quote(sc.NextAttempt)} IS NULL OR s.{Quote(sc.NextAttempt)} <= @now)
              AND (s.{Quote(sc.LockedUntil)} IS NULL OR s.{Quote(sc.LockedUntil)} <= @now)
            ORDER BY m.{Quote(mc.OccurredOn)} ASC
            LIMIT @batchSize
            FOR UPDATE SKIP LOCKED OF s
        )
        UPDATE {fullStateName} s
        SET {Quote(sc.Status)} = @processing,
            {Quote(sc.LockedUntil)} = @lockUntil,
            {Quote(sc.LastAttempt)} = @now,
            {Quote(sc.AttemptCount)} = s.{Quote(sc.AttemptCount)} + 1
        FROM cte
        WHERE s.{Quote(sc.Id)} = cte.{Quote(sc.Id)}
        RETURNING s.*;
        """;

        var pending = (int)MessageStatus.Pending;
        var processing = (int)MessageStatus.Processing;

        var query = dbContext.Set<InboxState>()
            .FromSqlRaw(sql,
                new NpgsqlParameter("@tenantId", tenantId),
                new NpgsqlParameter("@consumerName", consumerName),
                new NpgsqlParameter("@batchSize", batchSize),
                new NpgsqlParameter("@pending", pending),
                new NpgsqlParameter("@processing", processing),
                new NpgsqlParameter("@now", now),
                new NpgsqlParameter("@lockUntil", lockUntil))
            .Include(x => x.Message);

#if DEBUG
        Console.WriteLine($"Executing SqlServerSkipOutboxLockedStrategy query: {query.ToQueryString()}");
#endif

        // 3. Exécution avec inclusion de la Payload
        // .Include(x => x.Message) permet à EF de mapper les colonnes de la table liée 
        // si elles sont présentes ou via une seconde requête optimisée.
        return await query
            .AsNoTracking()  //Important car on a déjà fait l'update en SQL, pas besoin de tracking EF
            .ToListAsync(cancellationToken);
    }
}
