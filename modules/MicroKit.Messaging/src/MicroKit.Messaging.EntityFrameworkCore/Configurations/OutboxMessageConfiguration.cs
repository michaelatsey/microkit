using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>
/// EF Core entity configuration for <see cref="OutboxMessage"/>.
/// Apply via <see cref="ModelBuilderExtensions.ApplyMessagingConfiguration"/>.
/// </summary>
/// <remarks>
/// <para>
/// One decision here is a support declaration rather than a mapping preference, and it is stated
/// on the type for that reason: <c>UX_OutboxMessages_Origin_ContractName</c>, unique over
/// (<see cref="OutboxMessage.OriginMessageId"/>, <see cref="OutboxMessage.ContractName"/>), is
/// <b>supported on PostgreSQL and SQLite and NOT supported on SQL Server</b>.
/// </para>
/// <para>
/// Every <see cref="MessageKind.Notification"/> row carries <see langword="null"/> in both columns.
/// PostgreSQL and SQLite treat nulls as distinct in a unique index, so unlimited such rows coexist;
/// SQL Server treats them as equal, so the <b>second</b> notification row ever written is rejected
/// — in production, on the second insert, never at DDL time. The index carries a model invariant
/// rather than a performance hint, so it cannot be dropped to make an unsupported provider work.
/// See <see cref="ModelBuilderExtensions.ApplyMessagingConfiguration"/> and the module CHANGELOG.
/// </para>
/// </remarks>
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

        builder.Property(m => m.OriginMessageId)
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
        // same contract from the same origin row — so the second write collides here instead of
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
        // row staged outside a dispatch (no origin row) does not deduplicate. That is the intended
        // scope — the key guards the replay path, where an origin row always exists.
        //
        // FORWARD NOTE for the step-5 publisher, which is what will absorb a collision here:
        // this INSERT happens inside the caller's business transaction, and on PostgreSQL a unique
        // violation aborts the whole transaction, not just the statement. Catching DbUpdateException
        // and carrying on is therefore not enough — the pattern that works is already in this
        // package at EfInboxStore.AddAsync: take a savepoint before the insert, detach the entry and
        // roll back to it on violation, then verify post-hoc rather than decoding a provider error
        // code. Note the 24-character savepoint-name cap documented there; it exists for a provider
        // this index no longer supports, but the cap is cheap to keep and expensive to rediscover.
        builder.HasIndex(m => new { m.OriginMessageId, m.ContractName })
            .IsUnique()
            .HasDatabaseName("UX_OutboxMessages_Origin_ContractName");

        // Cleanup index: DeleteProcessedAsync filter on (TenantId, ProcessedAtUtc).
        builder.HasIndex(m => new { m.TenantId, m.ProcessedAtUtc })
            .HasDatabaseName("IX_OutboxMessages_TenantId_ProcessedAt");
    }
}
