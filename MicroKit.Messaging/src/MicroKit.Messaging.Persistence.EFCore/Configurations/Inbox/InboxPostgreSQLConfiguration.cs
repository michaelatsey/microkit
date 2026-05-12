using MicroKit.Messaging.Abstractions.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MicroKit.Messaging.Persistence.EFCore.Configurations.Inbox;

/// <summary>PostgreSQL-specific EF Core entity type configuration extensions for inbox entities.</summary>
public static class InboxPostgreSQLConfiguration
{
    /// <summary>Applies PostgreSQL-specific column types and indexes to the inbox message entity.</summary>
    /// <param name="builder">The entity type builder for <see cref="InboxMessage"/>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static EntityTypeBuilder<InboxMessage> ConfigurePostgreSQLInboxMessage(this EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("microkit_inbox_messages", "messaging");

        // =========================
        // PRIMARY KEY
        // =========================
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.MessageType)
            .HasColumnName("message_type")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.OccurredOnUtc)
            .HasColumnName("occurred_on_utc")
            .IsRequired();

        builder.Property(x => x.Headers)
            .HasColumnName("headers")
            .HasColumnType("jsonb");

        // GIN index headers (si routage par header)
        builder.HasIndex(x => x.Headers)
            .HasMethod("gin")
            .HasDatabaseName("ix_inbox_messages_headers_gin");

        return builder;
    }

    /// <summary>Applies PostgreSQL-specific column types and indexes to the inbox state entity.</summary>
    /// <param name="builder">The entity type builder for <see cref="InboxState"/>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static EntityTypeBuilder<InboxState> ConfigurePostgreSQInboxState(this EntityTypeBuilder<InboxState> builder)
    {
        builder.ToTable("microkit_inbox_states", "messaging");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.InboxMessageId)
            .HasColumnName("inbox_message_id")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.ConsumerName)
            .HasColumnName("consumer_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.ProcessingMetadata)
            .HasColumnName("processing_metadata")
            .HasColumnType("jsonb");

        builder.Property(x => x.LastError)
            .HasColumnType("text");

        // PostgreSQL native concurrency
        builder.Property(x => x.RowVersion)
           .IsRowVersion()
           .IsRequired();

        // Cascade delete via FK — no navigation properties in Abstractions
        builder.HasOne<InboxMessage>()
            .WithMany()
            .HasForeignKey(x => x.InboxMessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // =========================
        // 🔥 INDEX STRATEGY
        // =========================

        // 1️⃣ UNIQUE INDEX (Multi-tenant aware)
        // Garantit qu'un tenant ne traite pas deux fois le même message pour un même consommateur
        builder.HasIndex(x => new 
        { 
            x.TenantId, 
            x.InboxMessageId, 
            x.ConsumerName 
        })
        .IsUnique()
        .HasDatabaseName("ux_inboxstate_tenant_message_consumer");

        // 2️⃣ WORKER POLLING (Index Partiel optimisé)
        // C'est l'index le plus important. On filtre pour exclure les messages traités (ex: status = 2)
        // PostgreSQL gère très bien les index composites avec des filtres.
        builder.HasIndex(x => new 
        { 
            x.TenantId, 
            x.ConsumerName, 
            x.Status, 
            x.NextAttemptAtUtc, 
            x.LockedUntilUtc 
        })
        .HasFilter("status NOT IN (3, 5)")
        .HasDatabaseName("ix_inboxstate_polling_active");
        // 2️⃣ Partial index Pending only
        builder.HasIndex(x => new { x.ConsumerName, x.NextAttemptAtUtc })
            .HasDatabaseName("ix_inboxstate_pending_only")
            .HasFilter("status = 0");

        // 4️⃣ Retry optimization
        builder.HasIndex(x => new { x.Status, x.AttemptCount })
            .HasDatabaseName("ix_inboxstate_retry");

        builder.HasIndex(x => x.NextAttemptAtUtc)
            .HasDatabaseName("ix_inboxstate_scheduler")
            .HasFilter(@"""Status"" IN (0,4)");

        builder.HasIndex(x => x.ProcessedAtUtc)
        .HasFilter("status = 3")
        .HasDatabaseName("ix_inboxstate_cleanup");

        builder.HasIndex(x => x.LockedUntilUtc);

        builder.Property<string>("CorrelationId")
            .HasColumnName("correlation_id")
            .HasComputedColumnSql("headers->>'CorrelationId'", stored: true);

        builder.HasIndex("CorrelationId")
            .HasDatabaseName("ix_inbox_messages_correlation_id");

        return builder;
    }
}
