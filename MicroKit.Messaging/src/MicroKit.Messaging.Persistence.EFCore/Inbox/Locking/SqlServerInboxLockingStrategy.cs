using MicroKit.Messaging.Abstractions.Common;
using MicroKit.Messaging.Abstractions.Inbox;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MicroKit.Messaging.Persistence.EFCore.Inbox.Locking;

internal sealed class SqlServerInboxLockingStrategy : IInboxLockingStrategy
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

        var stateEntityType = dbContext.Model.FindEntityType(typeof(InboxState))
            ?? throw new InvalidOperationException("InboxState is not configured in the DbContext model.");
        var messageEntityType = dbContext.Model.FindEntityType(typeof(InboxMessage))
            ?? throw new InvalidOperationException("InboxMessage is not configured in the DbContext model.");

        string GetCol(IEntityType et, string prop) => et.FindProperty(prop)?.GetColumnName() ?? prop;

        var stateTable = stateEntityType.GetTableName();
        var stateSchema = stateEntityType.GetSchema();
        var msgTable = messageEntityType.GetTableName();
        var msgSchema = messageEntityType.GetSchema();

        var fullStateName = $"[{stateSchema}].[{stateTable}]";
        var fullMsgName = $"[{msgSchema}].[{msgTable}]";

        var s = new
        {
            Id = GetCol(stateEntityType, nameof(InboxState.Id)),
            Status = GetCol(stateEntityType, nameof(InboxState.Status)),
            LockedUntil = GetCol(stateEntityType, nameof(InboxState.LockedUntilUtc)),
            LastAttempt = GetCol(stateEntityType, nameof(InboxState.LastAttemptedAtUtc)),
            AttemptCount = GetCol(stateEntityType, nameof(InboxState.AttemptCount)),
            ConsumerName = GetCol(stateEntityType, nameof(InboxState.ConsumerName)),
            NextAttempt = GetCol(stateEntityType, nameof(InboxState.NextAttemptAtUtc)),
            TenantId = GetCol(stateEntityType, nameof(InboxState.TenantId)),
            MsgId = GetCol(stateEntityType, nameof(InboxState.InboxMessageId))
        };

        var m = new
        {
            Id = GetCol(messageEntityType, nameof(InboxMessage.Id)),
            OccurredOn = GetCol(messageEntityType, nameof(InboxMessage.OccurredOnUtc))
        };

        // UPDLOCK + READPAST for skip-locked semantics; JOIN message only for FIFO ordering
        var sql = $"""
            DECLARE @UpdatedIds TABLE (Id UNIQUEIDENTIFIER);

            WITH TargetCTE AS (
                SELECT TOP (@batchSize) s.[{s.Id}]
                FROM {fullStateName} AS s WITH (UPDLOCK, READPAST, ROWLOCK)
                INNER JOIN {fullMsgName} AS m ON s.[{s.MsgId}] = m.[{m.Id}]
                WHERE s.[{s.TenantId}] = @tenantId
                  AND s.[{s.ConsumerName}] = @consumerName
                  AND s.[{s.Status}] = @pending
                  AND (s.[{s.NextAttempt}] IS NULL OR s.[{s.NextAttempt}] <= @now)
                  AND (s.[{s.LockedUntil}] IS NULL OR s.[{s.LockedUntil}] <= @now)
                ORDER BY m.[{m.OccurredOn}] ASC
            )
            UPDATE {fullStateName}
            SET [{s.Status}] = @processing,
                [{s.LockedUntil}] = @lockUntil,
                [{s.LastAttempt}] = @now,
                [{s.AttemptCount}] = [{s.AttemptCount}] + 1
            OUTPUT INSERTED.[{s.Id}] INTO @UpdatedIds
            FROM TargetCTE
            WHERE {fullStateName}.[{s.Id}] = TargetCTE.[{s.Id}];

            SELECT s.*
            FROM {fullStateName} s
            WHERE s.[{s.Id}] IN (SELECT Id FROM @UpdatedIds);
            """;

        var pending = (int)MessageStatus.Pending;
        var processing = (int)MessageStatus.Processing;

        var query = dbContext.Set<InboxState>()
            .FromSqlRaw(sql,
                new SqlParameter("@tenantId", tenantId),
                new SqlParameter("@consumerName", consumerName),
                new SqlParameter("@batchSize", batchSize),
                new SqlParameter("@pending", pending),
                new SqlParameter("@processing", processing),
                new SqlParameter("@now", now),
                new SqlParameter("@lockUntil", lockUntil));

#if DEBUG
        Console.WriteLine($"Executing SqlServerInboxLockingStrategy query: {query.ToQueryString()}");
#endif

        return await query
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
