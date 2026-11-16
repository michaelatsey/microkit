using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MicroKit.Messaging.EntityFrameworkCore;

/// <summary>
/// EF Core entity configuration for <see cref="OutboxMessage"/>.
/// Apply via <see cref="ModelBuilderExtensions.ApplyMessagingConfiguration"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OutboxMessage.Headers"/> is stored as one JSON object in a text column; the other
/// columns map one to one. The host owns the migrations: none is shipped.
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

        // 256 is the wire contract name's own bound: a name that must survive a CLR rename, an
        // assembly split and a module extraction is written by hand and read by strangers, so it
        // is short by construction. Pinned by a test, because a consumer's contract names are data
        // this column silently truncates on a provider that does not enforce width.
        builder.Property(m => m.ContractName)
            .HasMaxLength(256);

        // Not IsRequired: null on a Notification row, which never leaves the process and so has no
        // emitter to declare. TransportOutboxDispatcher enforces non-null on the Contract path,
        // where the entity cannot.
        builder.Property(m => m.Source)
            .HasMaxLength(256);

        // The publication's own headers, as one JSON object, with the shared value comparer.
        builder.Property(m => m.Headers)
            .HasConversion(
                v => HeadersJson.Write(v),
                v => HeadersJson.Read(v),
                HeadersJson.Comparer);

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

        // A W3C traceparent is 55 characters in the current version; 64 leaves room for the next
        // one without inviting anything else into the column. Null on every Notification row.
        builder.Property(m => m.TraceParent)
            .HasMaxLength(64);

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

        // Mapped explicitly rather than by convention, because it carries weight: this is the
        // claim's sort key. The value is the WRITER's responsibility — nothing defaults it — and a
        // row written straight through IOutboxWriter with the property omitted persists
        // 0001-01-01, which now heads the queue permanently rather than merely carrying a wrong
        // timestamp. OutboxMessageFactory stamps it from TimeProvider on both paths.
        builder.Property(m => m.CreatedAtUtc)
            .IsRequired();

        // No HasQueryFilter — infrastructure table, read cross-tenant by processors (ADR-MSG-002).

        // Claim index: ClaimBatchAsync candidate filter + OrderBy(CreatedAtUtc). Column order
        // follows the predicate — DeadLettered and Status are equality-ish, NextRetryAtUtc is a
        // range, CreatedAtUtc supplies the sort.
        //
        // The sort column is CreatedAtUtc and not OccurredOnUtc, and the two must stay in step with
        // EfOutboxStore's OrderBy clauses: ordering the queue on a caller-supplied business
        // timestamp lets a backdated event jump every row ahead of it, and on the contract path
        // that timestamp is an optional parameter of a public method.
        //
        // Deliberately NOT a filtered index. A partial index (`WHERE dead_lettered = false`)
        // would keep it small as published rows accumulate, but HasFilter takes provider-specific
        // SQL and this is the provider-neutral EF Core package. Consumers who own their DDL can
        // add the filtered variant — see the README.
        builder.HasIndex(m => new { m.DeadLettered, m.Status, m.NextRetryAtUtc, m.CreatedAtUtc })
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
