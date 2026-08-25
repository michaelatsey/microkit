using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>
/// EF Core entity configuration for <see cref="IntegrationEventMessage"/>.
/// Apply via <see cref="ModelBuilderExtensions.ApplyMessagingConfiguration"/>.
/// </summary>
/// <remarks>
/// One decision here is a correctness guarantee rather than a mapping preference, and it is
/// configured centrally for that reason: removing it breaks no test that does not exercise
/// concurrency. See the <c>ClaimToken</c> mapping below.
/// </remarks>
public sealed class IntegrationEventMessageConfiguration
    : IEntityTypeConfiguration<IntegrationEventMessage>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<IntegrationEventMessage> builder)
    {
        builder.ToTable("IntegrationEventMessages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasConversion(new ValueConverter<MessageId, Guid>(
                v => v.Value,
                v => new MessageId(v)));

        builder.Property(m => m.ContractName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.Source)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.Data)
            .IsRequired();

        builder.Property(m => m.TenantId)
            .HasMaxLength(256);
        // TenantId is intentionally optional (no IsRequired) — Messaging must operate
        // without Tenancy per ADR-EXEC-001; host enforces TenantId when needed.

        builder.Property(m => m.CorrelationId)
            .HasConversion(new ValueConverter<CorrelationId?, Guid?>(
                v => v == null ? null : v.Value,
                v => v == null ? null : new CorrelationId(v.Value)));

        builder.Property(m => m.CausationId)
            .HasConversion(new ValueConverter<CausationId?, Guid?>(
                v => v == null ? null : v.Value,
                v => v == null ? null : new CausationId(v.Value)));

        // A W3C traceparent is 55 characters; 64 leaves room without inviting anything else in.
        builder.Property(m => m.TraceParent)
            .HasMaxLength(64);

        builder.Property(m => m.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(m => m.ErrorMessage)
            .HasMaxLength(2048);

        // ClaimToken is a concurrency token, for the same reason it is one on the inbox: without
        // it the UPDATE that SaveChanges emits carries only the primary key, so a relay whose
        // lease expired mid-delivery would overwrite the relay that legitimately took the message
        // over. Consumed by the relay, which is a later lot; mapped here because the column
        // belongs to the row, and because leaving it to that lot means the day someone forgets,
        // no test that does not exercise concurrency will notice.
        //
        // Note the deliberate asymmetry with OutboxMessageConfiguration, which maps ClaimToken as
        // a plain Guid?: the outbox settles through ExecuteUpdateAsync, which bypasses the change
        // tracker and carries its own token filter, so a concurrency token would buy it nothing.
        builder.Property(m => m.ClaimToken)
            .IsConcurrencyToken();

        // No HasQueryFilter — infrastructure table, read cross-tenant by the relay (ADR-MSG-002).

        // Claim index: eligibility filter + ordering. Equality predicates lead, the range column
        // and then the sort column trail.
        //
        // CreatedAtUtc, never OccurredOnUtc: ordering on the business timestamp would let a
        // backdated event jump the whole queue, and would make queue order depend on data a
        // caller supplies.
        //
        // Deliberately NOT a filtered index. A partial index (`WHERE DeadLettered = false`) would
        // keep it small as published rows accumulate, but HasFilter takes provider-specific SQL
        // and this is the provider-neutral EF Core package. Consumers who own their DDL can add
        // the filtered variant.
        builder.HasIndex(m => new { m.DeadLettered, m.Status, m.NextRetryAtUtc, m.CreatedAtUtc })
            .HasDatabaseName("IX_IntegrationEventMessages_Dispatchable");

        // Read-back by token, and lease recovery after a crashed relay.
        builder.HasIndex(m => m.ClaimToken)
            .HasDatabaseName("IX_IntegrationEventMessages_ClaimToken");

        // Retention sweeps delivered rows by age.
        builder.HasIndex(m => new { m.TenantId, m.ProcessedAtUtc })
            .HasDatabaseName("IX_IntegrationEventMessages_TenantId_ProcessedAt");
    }
}
