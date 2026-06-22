using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>
/// EF Core entity configuration for <see cref="InboxMessage"/>.
/// Apply via <see cref="ModelBuilderExtensions.ApplyMessagingConfiguration"/>.
/// </summary>
public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("InboxMessages");

        // Composite PK (MessageId, ConsumerType) is the authoritative dedup unique constraint.
        builder.HasKey(m => new { m.MessageId, m.ConsumerType });

        builder.Property(m => m.MessageId)
            .HasConversion(new ValueConverter<MessageId, Guid>(
                v => v.Value,
                v => new MessageId(v)));

        builder.Property(m => m.ConsumerType)
            .IsRequired()
            .HasMaxLength(512);

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
            .HasConversion(new ValueConverter<CorrelationId?, Guid?>(
                v => v == null ? null : v.Value,
                v => v == null ? null : new CorrelationId(v.Value)));

        builder.Property(m => m.CausationId)
            .HasConversion(new ValueConverter<CausationId?, Guid?>(
                v => v == null ? null : v.Value,
                v => v == null ? null : new CausationId(v.Value)));

        // No HasQueryFilter — infrastructure table, read cross-tenant by processors (ADR-MSG-002).

        // Polling index: GetPendingAsync filter on (Status, NextRetryAtUtc, LockedUntilUtc).
        builder.HasIndex(m => new { m.Status, m.NextRetryAtUtc, m.LockedUntilUtc })
            .HasDatabaseName("IX_InboxMessages_Status_NextRetryAt_LockedUntil");
    }
}
