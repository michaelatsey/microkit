    using MicroKit.Messaging.Abstractions.Common;
    using MicroKit.Messaging.Abstractions.Outbox;
    using Microsoft.Data.SqlClient;
    using Microsoft.EntityFrameworkCore;
    using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

    namespace MicroKit.Messaging.Persistence.EFCore.Outbox.Locking;

    internal sealed class SqlServerSkipOutboxLockedStrategy : IOutboxLockingStrategy
    {
        public async Task<IReadOnlyList<OutboxMessage>> LockNextAsync(
            DbContext dbContext,
            string tenantId,
            int batchSize,
            TimeSpan lockDuration,
            CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var lockUntil = now.Add(lockDuration);

            // 1. Extraction dynamique des métadonnées EF Core
            var entityType = dbContext.Model.FindEntityType(typeof(OutboxMessage))
                ?? throw new InvalidOperationException("OutboxMessage non configuré.");

            string GetCol(string prop) => entityType.FindProperty(prop)?.GetColumnName() ?? prop;
            string Quote(string ident) => $"[{ident}]"; // SQL Server utilise les crochets

            var table = entityType.GetTableName();
            var schema = entityType.GetSchema();
            var fullName = string.IsNullOrEmpty(schema) ? Quote(table!) : $"{Quote(schema)}.{Quote(table!)}";

            // Mapping des colonnes
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

            // 2. SQL avec CTE, Hints de verrouillage et OUTPUT
            // READPAST : Saute les lignes déjà verrouillées (Skip Locked)
            // UPDLOCK : Pose un verrou de mise à jour pour empêcher d'autres lectures concurrentes
            var sql = $"""
            WITH cte AS (
                SELECT TOP (@batchSize) {Quote(c.Id)}
                FROM {fullName} WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE {Quote(c.TenantId)} = @tenantId
                  AND (
                      {Quote(c.Status)} = @pending 
                      OR ({Quote(c.Status)} = @processing AND {Quote(c.LockedUntil)} <= @now)
                  )
                  AND ({Quote(c.ScheduledAt)} IS NULL OR {Quote(c.ScheduledAt)} <= @now)
                ORDER BY {Quote(c.OccurredOn)} ASC
            )
            UPDATE m
            SET {Quote(c.Status)} = @processing,
                {Quote(c.LockedUntil)} = @lockUntil,
                {Quote(c.LastAttempt)} = @now,
                {Quote(c.RetryCount)} = m.{Quote(c.RetryCount)} + 1
            OUTPUT INSERTED.*
            FROM {fullName} m
            INNER JOIN cte ON m.{Quote(c.Id)} = cte.{Quote(c.Id)};
            """;
            var pending = (int)MessageStatus.Pending;
            var processing = (int)MessageStatus.Processing;

            var parameters = new[]
            {
                new SqlParameter("@tenantId", tenantId),
                new SqlParameter("@batchSize", batchSize),
                new SqlParameter("@pending", pending),
                new SqlParameter("@processing", processing),
                new SqlParameter("@now", now),
                new SqlParameter("@lockUntil", lockUntil)
            };
            var query = dbContext.Set<OutboxMessage>()
                .FromSqlRaw(sql, parameters);
    #if DEBUG
            Console.WriteLine($"Executing SqlServerSkipOutboxLockedStrategy query: {query.ToQueryString()}");
    #endif
            return await query
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }
    }
