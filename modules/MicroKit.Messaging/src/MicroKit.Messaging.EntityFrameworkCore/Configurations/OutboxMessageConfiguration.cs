using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>
/// EF Core entity configuration for <see cref="OutboxMessage"/>.
/// Apply via <see cref="ModelBuilderExtensions.ApplyMessagingConfiguration"/>.
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasConversion(new ValueConverter<MessageId, Guid>(
                v => v.Value,
                v => new MessageId(v)));

        builder.Property(m => m.TenantId)
            .HasMaxLength(256);
        // TenantId is intentionally optional (no IsRequired) — Messaging must operate
        // without Multitenancy per ADR-EXEC-001; host enforces TenantId when needed.

        // Stored as a string, like every other enum in this module: the column exists to be
        // queried, and an int discriminator is unreadable in psql or the Supabase dashboard.
        // It also means inserting an enum member cannot silently remap existing rows.
        builder.Property(m => m.MessageKind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        // 256 matches IntegrationEventMessageConfiguration.ContractName — the same notion, and the
        // two must not disagree while both are live.
        builder.Property(m => m.ContractName)
            .HasMaxLength(256);

        builder.Property(m => m.SourceMessageId)
            .HasConversion(new ValueConverter<MessageId?, Guid?>(
                v => v == null ? null : v.Value,
                v => v == null ? null : new MessageId(v.Value)));

        builder.Property(m => m.EventType)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(m => m.Payload)
            .IsRequired();

        builder.Property(m => m.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(m => m.ErrorMessage)
            .HasMaxLength(2048);

        builder.Property(m => m.CorrelationId)
            .IsRequired()
            .HasConversion(new ValueConverter<CorrelationId, Guid>(
                v => v.Value,
                v => new CorrelationId(v)));

        builder.Property(m => m.CausationId)
            .HasConversion(new ValueConverter<CausationId?, Guid?>(
                v => v == null ? null : v.Value,
                v => v == null ? null : new CausationId(v.Value)));

        // Plain Guid? — no value converter, no invariant. Written by ClaimBatchAsync and
        // cleared by every terminal write.
        builder.Property(m => m.ClaimToken);

        // No HasQueryFilter — infrastructure table, read cross-tenant by processors (ADR-MSG-002).

        // Claim index: ClaimBatchAsync candidate filter + OrderBy(OccurredOnUtc). Column order
        // follows the predicate — DeadLettered and Status are equality-ish, NextRetryAtUtc is a
        // range, OccurredOnUtc supplies the sort.
        //
        // Deliberately NOT a filtered index. A partial index (`WHERE dead_lettered = false`)
        // would keep it small as published rows accumulate, but HasFilter takes provider-specific
        // SQL and this is the provider-neutral EF Core package. Consumers who own their DDL can
        // add the filtered variant — see the README.
        builder.HasIndex(m => new { m.DeadLettered, m.Status, m.NextRetryAtUtc, m.OccurredOnUtc })
            .HasDatabaseName("IX_OutboxMessages_Dispatchable");

        // Read-back index: the contended branch of ClaimBatchAsync selects by token alone.
        builder.HasIndex(m => m.ClaimToken)
            .HasDatabaseName("IX_OutboxMessages_ClaimToken");

        // Legacy polling index, retained: still covers the (Status, NextRetryAtUtc) prefix used
        // by ad-hoc operator queries.
        builder.HasIndex(m => new { m.Status, m.NextRetryAtUtc, m.LockedUntilUtc })
            .HasDatabaseName("IX_OutboxMessages_Status_NextRetryAt_LockedUntil");

        // The replay natural key. A redelivered dispatch re-runs its handlers, which publish the
        // same contract from the same source row — so the second write collides here instead of
        // producing a duplicate integration message.
        //
        // UNFILTERED, and that is a support declaration rather than an oversight. A notification
        // row has both columns null, and unlimited such rows must coexist:
        //
        //   PostgreSQL  nulls are DISTINCT in a unique index (NULLS NOT DISTINCT is opt-in,
        //               PG15+, and is not used here)              -> supported
        //   SQLite      same rule, explicitly documented           -> supported
        //   SQL Server  nulls compare EQUAL, so the SECOND notification row ever written is
        //               rejected                                   -> NOT SUPPORTED
        //
        // SQL Server is not supported for this constraint. The remedy there is a filtered index
        // (WHERE both columns IS NOT NULL), which cannot live here: HasFilter takes provider-
        // specific SQL and this is the provider-neutral package. Unlike the size-oriented partial
        // indexes noted above, this one carries a model invariant, so it is declared rather than
        // left to a consumer to reconstruct. See the README and CHANGELOG.
        //
        // Note what the null semantics also mean: a NULL on either side is DISTINCT, so a contract
        // row staged outside a dispatch (no source row) does not deduplicate. That is the intended
        // scope — the key guards the replay path, where a source row always exists.
        builder.HasIndex(m => new { m.SourceMessageId, m.ContractName })
            .IsUnique()
            .HasDatabaseName("UX_OutboxMessages_Source_ContractName");

        // Cleanup index: DeleteProcessedAsync filter on (TenantId, ProcessedAtUtc).
        builder.HasIndex(m => new { m.TenantId, m.ProcessedAtUtc })
            .HasDatabaseName("IX_OutboxMessages_TenantId_ProcessedAt");
    }
}
