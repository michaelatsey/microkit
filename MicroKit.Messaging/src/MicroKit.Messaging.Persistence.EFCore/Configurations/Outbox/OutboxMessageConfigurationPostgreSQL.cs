using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MicroKit.Messaging.Abstractions.Outbox;
namespace MicroKit.Messaging.Persistence.EFCore.Configurations.Outbox;

public static class OutboxMessageConfigurationPostgreSQL
{
    public static EntityTypeBuilder<OutboxMessage> PostgreSQLConfigure(this EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("microkit_outbox_messages", schema: "messaging");

        // =========================
        // PRIMARY KEY
        // =========================
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever(); // On génère côté application

        // =========================
        // 🔥 MULTI-TENANCY (INDISPENSABLE)
        // =========================
        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasMaxLength(64)
            .IsRequired();

        // =========================
        // ✉️ ENVELOPE METADATA (À PLAT)
        // =========================
        builder.Property(x => x.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(128);

        builder.Property(x => x.CausationId)
            .HasColumnName("causation_id")
            .HasMaxLength(128);

        builder.Property(x => x.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(256);

        // =========================
        // CORE FIELDS
        // =========================
        builder.Property(x => x.MessageType)
            .HasColumnName("message_type")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb") // Ultra important pour PostgreSQL
            .IsRequired();

        builder.Property(x => x.OccurredOnUtc)
            .HasColumnName("occurred_on_utc")
            .IsRequired();

        builder.Property(x => x.ProcessedAtUtc)
            .HasColumnName("processed_at_utc");

        builder.Property(x => x.ScheduledAtUtc)
            .HasColumnName("scheduled_at_utc");

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<int>() // Stockage int pour performance index
            .IsRequired();

        builder.Property(x => x.RetryCount)
            .HasColumnName("retry_count")
            .HasDefaultValue(0);

        builder.Property(x => x.Error)
            .HasColumnName("error")
            .HasColumnType("text");

        // =========================
        // OPTIMISTIC CONCURRENCY
        // =========================
        // Utilise la colonne système PostgreSQL xmin (meilleure pratique)
        builder.Property(x => x.RowVersion)
        .IsRowVersion();
        //builder.UseXminAsConcurrencyToken();

        // =========================
        // OWNED TYPE : Destination
        // =========================
        builder.Property(x => x.PublishAsNotification).HasColumnName("publish_as_notification");
        builder.Property(x => x.PublishToBroker).HasColumnName("publish_to_broker");
        builder.Property(x => x.BrokerTopic).HasColumnName("broker_topic").HasMaxLength(250);
        builder.Property(x => x.PartitionKey).HasColumnName("partition_key").HasMaxLength(250);

        builder.Property(x => x.Metadata)
            .HasColumnName("destination_metadata")
            .HasColumnType("jsonb");

        // =========================
        //  INDEXES STRATÉGIQUES
        // =========================

        // 1️ Index principal pour worker polling
        builder.HasIndex(x => new { x.TenantId, x.Status, x.ScheduledAtUtc, x.OccurredOnUtc })
            .HasDatabaseName("ix_outbox_polling");

        // Dans ta configuration d'index
        builder.HasIndex(x => new { x.TenantId, x.Status, x.LockedUntilUtc, x.ScheduledAtUtc })
            .HasDatabaseName("IX_Outbox_Tenant_Lease_Polling");

        // 2️ Index partiel ULTRA optimisé pour Pending uniquement
        builder.HasIndex(x => new { x.TenantId, x.ScheduledAtUtc, x.OccurredOnUtc })
            .HasDatabaseName("ix_outbox_pending_only")
            .HasFilter("status = 0"); // 0 = Pending

        // 3️ Index pour retry strategy
        builder.HasIndex(x => new { x.Status, x.RetryCount })
            .HasDatabaseName("ix_outbox_retry");

        // 4️ Index pour monitoring / dashboard
        builder.HasIndex(x => x.ProcessedAtUtc)
            .HasDatabaseName("ix_outbox_processed_at");

        // 5 Index GIN pour recherche JSONB (metadata)
        builder.HasIndex("metadata")
            .HasMethod("gin")
            .HasDatabaseName("ix_outbox_metadata_gin");

        // 6️ Index message type pour analytics / debug
        builder.HasIndex(x => x.MessageType)
            .HasDatabaseName("ix_outbox_message_type");
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
        .HasDatabaseName("ix_outbox_tenant_idempotency")
        .HasFilter("idempotency_key IS NOT NULL");

        // 4️⃣ Index de tracking (Correlation)
        builder.HasIndex(x => x.CorrelationId)
            .HasDatabaseName("ix_outbox_correlation")
            .HasFilter("correlation_id IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
            .HasDatabaseName("UX_Outbox_Tenant_Idempotency")
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL"); // Attention aux doubles quotes sur Postgres  

        return builder;
    }
}
