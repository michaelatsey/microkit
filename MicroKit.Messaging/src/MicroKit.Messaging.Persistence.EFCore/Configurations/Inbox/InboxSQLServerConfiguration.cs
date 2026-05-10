    using MicroKit.Messaging.Abstractions.Inbox;
    using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
    using System.Text.Json;

    namespace MicroKit.Messaging.Persistence.EFCore.Configurations.Inbox;


    public static class InboxSQLServerConfiguration 
    {

        public static EntityTypeBuilder<InboxMessage> ConfigureSQLServerInboxMessage(this EntityTypeBuilder<InboxMessage> builder)
        {

        builder.ToTable("MicroKit_InboxMessages", "messaging");

            // =========================
            // PRIMARY KEY
            // =========================
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .HasMaxLength(200)
                .IsUnicode(false)
                .ValueGeneratedNever();

            builder.Property(x => x.TenantId)
                .IsUnicode(false)
                .HasMaxLength(100)
                .IsRequired();

            // =========================
            // CORE
            // =========================
            builder.Property(x => x.MessageType)
                .HasMaxLength(500)
                .IsRequired();

            builder.Property(x => x.Payload)
                .HasColumnType("nvarchar(max)")
                .IsRequired();

            builder.Property(x => x.OccurredOnUtc)
                .HasPrecision(3)
                .IsRequired();

            var dictionaryComparer = new ValueComparer<Dictionary<string, string>>(
            // Égalité : On vérifie les nulls avant SequenceEqual
            (c1, c2) => c1 == null ? c2 == null : c2 != null && c1.SequenceEqual(c2),

            // HashCode : Gestion du null pour éviter NullReferenceException
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.Key.GetHashCode(), v.Value.GetHashCode())),

            // Snapshot (Deep Copy) : On retourne null si la source est nulle
            c => c == null ? null! : c.ToDictionary(entry => entry.Key, entry => entry.Value)
            );

        builder.Property(x => x.Headers)
                .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
                        v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions)null!) ?? new())
                .HasColumnType("nvarchar(max)")
                .Metadata.SetValueComparer(dictionaryComparer);

            builder.HasIndex(x => new { x.TenantId, x.Id })
                .IsUnique()
                .HasDatabaseName("UX_InboxMessage_Tenant_Message");

            builder.HasIndex(x => x.OccurredOnUtc)
                .HasDatabaseName("IX_InboxMessage_OccurredOn");

            return builder;
        }

        public static EntityTypeBuilder<InboxState> ConfigureSQLServerInboxState(this EntityTypeBuilder<InboxState> builder)
        {
            builder.ToTable("MicroKit_InboxStates", "messaging");

            var dictionaryComparer = new ValueComparer<Dictionary<string, string>>(
            // Égalité : On vérifie les nulls avant SequenceEqual
            (c1, c2) => c1 == null ? c2 == null : c2 != null && c1.SequenceEqual(c2),

            // HashCode : Gestion du null pour éviter NullReferenceException
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.Key.GetHashCode(), v.Value.GetHashCode())),

            // Snapshot (Deep Copy) : On retourne null si la source est nulle
            c => c == null ? null! : c.ToDictionary(entry => entry.Key, entry => entry.Value)
            );

        // =========================
        // PRIMARY KEY
        // =========================
        builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .ValueGeneratedNever(); // Guid généré côté app

        builder.Property(x => x.TenantId)
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();

            builder.Property(x => x.InboxMessageId)
                .HasMaxLength(200)
                .IsUnicode(false)
                .IsRequired();

            // =========================
            // CORE
            // =========================

            builder.Property(x => x.ConsumerName)
                .HasMaxLength(200)
                .IsUnicode(false)
                .IsRequired();

            builder.Property(x => x.Status)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.ProcessingMetadata)
                .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
                        v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions)null!) ?? new())
                .HasColumnType("nvarchar(max)")
                .Metadata.SetValueComparer(dictionaryComparer);

            builder.Property(x => x.LastError)
                .HasColumnType("nvarchar(max)");


            builder.Property(x => x.ProcessedAtUtc)
                .HasPrecision(3);

            builder.Property(x => x.LastAttemptedAtUtc)
                .HasPrecision(3)
                .IsRequired(false);

            builder.Property(x => x.NextAttemptAtUtc)
                .HasPrecision(3)
                .IsRequired(false);

            builder.Property(x => x.LockedUntilUtc)
                .HasPrecision(3)
                .IsRequired(false);

            builder.Property(x => x.ProcessedAtUtc)
                .HasPrecision(3)
                .IsRequired(false);
        

            builder.Property(x => x.AttemptCount)
                .HasDefaultValue(0);

            builder.Property(x => x.LastError)
                .HasColumnType("nvarchar(max)");

            // =========================
            // Concurrency
            // =========================
            builder.Property(x => x.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            // Navigation relationship
            builder.HasOne(x => x.Message)
                .WithMany(m => m.InboxStates)
                .HasForeignKey(x => x.InboxMessageId)
                .OnDelete(DeleteBehavior.Cascade);


            // =========================
            // INDEX STRATEGY
            // =========================

            // UNIQUE INDEX : Un message ne peut être traité qu'une seule fois par un consommateur donné
            builder.HasIndex(x => new { x.InboxMessageId, x.ConsumerName, x.TenantId })
                .IsUnique()
                .HasDatabaseName("UX_InboxState_Message_Tenant_Consumer");

            // Polling
            builder.HasIndex(x => new
            {
                x.TenantId,
                x.ConsumerName,
                x.Status,
                x.NextAttemptAtUtc,
                x.LockedUntilUtc
            })
            .HasFilter("[Status] <> 3 AND [Status] <> 5")
            .HasDatabaseName("IX_InboxState_Polling_ActiveOnly");

            //Filtered index Pending only
            builder.HasIndex(x => new {x.ConsumerName,x.NextAttemptAtUtc})
                .HasFilter("[Status] = 0")
                .HasDatabaseName("IX_InboxState_PendingOnly");

            builder.HasIndex(x => new { x.InboxMessageId, x.ConsumerName })
                .IsUnique()
                .HasDatabaseName("UX_InboxState_Message_Consumer");

            // Retry optimization
            builder.HasIndex(x => new {x.Status,x.AttemptCount})
                .HasFilter("[Status] = 4") // 4 = Failed
                .HasDatabaseName("IX_InboxState_Retry");

            // Monitoring
            builder.HasIndex(x => x.ProcessedAtUtc)
                .HasFilter("[Status] = 3")
                .HasDatabaseName("IX_Inbox_ProcessedAt");

            //builder.Property<string>("CorrelationId")
            //    .HasComputedColumnSql("JSON_VALUE([Headers], '$.CorrelationId')");

            //builder.HasIndex("CorrelationId");

            return builder;
        }
    }