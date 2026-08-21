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

        // Cleanup index: DeleteProcessedAsync filter on (TenantId, ProcessedAtUtc).
        builder.HasIndex(m => new { m.TenantId, m.ProcessedAtUtc })
            .HasDatabaseName("IX_OutboxMessages_TenantId_ProcessedAt");
    }
}
