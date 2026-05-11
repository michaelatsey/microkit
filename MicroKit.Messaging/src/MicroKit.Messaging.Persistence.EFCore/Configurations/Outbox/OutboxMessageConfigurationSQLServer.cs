using MicroKit.Messaging.Abstractions.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace MicroKit.Messaging.Persistence.EFCore.Configurations.Outbox;

public static class OutboxMessageConfigurationSQLServer
{
    public static EntityTypeBuilder<OutboxMessage> SQLServerConfigure(this EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("MicroKit_OutboxMessages", "messaging");

        // =========================
        // PRIMARY KEY
        // =========================
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        // =========================
        // MULTI-TENANCY
        // =========================
        builder.Property(x => x.TenantId)
            .HasMaxLength(100)
            .IsRequired();

        // =========================
        // ENVELOPE METADATA (A PLAT)
        // =========================
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.CausationId).HasMaxLength(128);
        builder.Property(x => x.IdempotencyKey).IsUnicode(false).HasMaxLength(256);

        // =========================
        // CORE FIELDS
        // =========================
        builder.Property(x => x.MessageType)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.Payload)
            .HasColumnType("nvarchar(max)") // JSON stocké en NVARCHAR
            .IsRequired();

        // Ajout d'une contrainte ISJSON pour SQL Server (Expert Tip)
        builder.ToTable(t => t.HasCheckConstraint("CK_Outbox_Payload_JSON", "ISJSON([Payload]) > 0"));

        builder.Property(x => x.OccurredOnUtc)
            .HasPrecision(3) // millisecond precision optimale
            .IsRequired();

        builder.Property(x => x.ProcessedAtUtc)
            .HasPrecision(3);

        builder.Property(x => x.ScheduledAtUtc)
            .HasPrecision(3);

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.RetryCount)
            .HasDefaultValue(0);

        builder.Property(x => x.Error)
            .HasColumnType("nvarchar(max)");

        // =========================
        // OPTIMISTIC CONCURRENCY
        // =========================
        builder.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        // =========================
        // OWNED TYPE : Destination
        // =========================

        // Mapping des propriétés de destination
        builder.Property(x => x.PublishAsNotification).HasDefaultValue(true);
        builder.Property(x => x.PublishToBroker).HasMaxLength(250).HasDefaultValue(true);
        builder.Property(x => x.BrokerTopic).HasMaxLength(250).IsRequired(false);
        builder.Property(x => x.PartitionKey).HasMaxLength(250).IsRequired(false);
        builder.Property(x => x.Metadata).HasColumnType("nvarchar(max)").IsRequired(false);

        // =========================
        // 🔥 INDEX STRATEGY
        // =========================

        // 1️⃣ Index principal polling
        builder.HasIndex(x => new { x.TenantId, x.Status, x.ScheduledAtUtc, x.OccurredOnUtc })
            .HasDatabaseName("IX_Outbox_Polling");

        // 2️⃣ Index filtré ultra performant (Pending only)
        builder.HasIndex(x => new { x.TenantId, x.ScheduledAtUtc, x.OccurredOnUtc })
            .HasDatabaseName("IX_Outbox_PendingOnly")
            .HasFilter("[Status] = 0");

        // Dans ta configuration d'index
        builder.HasIndex(x => new { x.TenantId, x.Status, x.LockedUntilUtc, x.ScheduledAtUtc })
            .HasDatabaseName("IX_Outbox_Tenant_Lease_Polling");

        // 3️⃣ Retry optimization
        builder.HasIndex(x => new { x.Status, x.RetryCount })
            .HasDatabaseName("IX_Outbox_Retry");

        // 4️⃣ Monitoring / dashboard
        builder.HasIndex(x => x.ProcessedAtUtc)
            .HasDatabaseName("IX_Outbox_ProcessedAt");

        // 5️⃣ MessageType analytics
        builder.HasIndex(x => x.MessageType)
            .HasDatabaseName("IX_Outbox_MessageType");

        // 3Index de recherche par IdempotencyKey (Protection contre les doublons)
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
            .HasDatabaseName("IX_Outbox_Tenant_Idempotency")
            .HasFilter("[IdempotencyKey] IS NOT NULL");

        // 4Monitoring Correlation
        builder.HasIndex(x => x.CorrelationId)
            .HasDatabaseName("IX_Outbox_Correlation")
            .HasFilter("[CorrelationId] IS NOT NULL");

        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
            .HasDatabaseName("UX_Outbox_Tenant_Idempotency")
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL");

        return builder;
    }
}
