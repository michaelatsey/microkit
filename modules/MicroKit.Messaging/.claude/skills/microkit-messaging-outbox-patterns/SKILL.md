# Skill: microkit-messaging-outbox-patterns

Implementation guide for the transactional outbox/inbox pattern in MicroKit.Messaging.

> **Rules govern constraints. Skills illustrate implementation. Where they conflict, rules win.**
> Canonical contracts: `microkit-messaging-outbox-inbox.md`

---

## Transactional Outbox — Implementation Guide

### Step 1: Write OutboxMessage in the same DB transaction as the domain commit

```csharp
// ✅ CORRECT — outbox message written atomically with business data
public sealed class PlaceOrderHandler(
    IOrderRepository orderRepo,
    IOutboxWriter outboxWriter,          // ← IOutboxWriter, not IOutboxProcessorStore
    IUnitOfWork uow)
{
    public async ValueTask<Result<OrderId>> HandleAsync(PlaceOrderCommand cmd, CancellationToken ct = default)
    {
        var order = Order.Create(cmd.CustomerId, cmd.Items);
        await orderRepo.AddAsync(order, ct).ConfigureAwait(false);

        // Write outbox message BEFORE commit — same transaction boundary
        var evt = new OrderPlacedEvent(order.Id, order.TenantId, order.OccurredOnUtc);
        var envelope = MessageEnvelope.Create(evt, correlationId: cmd.CorrelationId);
        await outboxWriter.AddAsync(OutboxMessage.FromEnvelope(envelope), ct).ConfigureAwait(false);

        // Single commit — order row + outbox row are atomic
        await uow.CommitAsync(ct).ConfigureAwait(false);

        return Result.Ok(order.Id);
    }
}

// ❌ WRONG — publish event before commit — message sent but order save may fail
await publisher.PublishAsync(evt, ct);  // ← broker gets message
await uow.CommitAsync(ct);              // ← DB commit fails → order lost, event orphaned
```

---

### Step 2: OutboxProcessor polls and dispatches (scope-per-message)

```csharp
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxProcessor> logger,
    IOptions<OutboxProcessorOptions> options) : BackgroundService
{
    private readonly OutboxProcessorOptions _opts = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxProcessor batch failed — will retry after delay");
            }

            await Task.Delay(_opts.PollingInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task ProcessBatchAsync(CancellationToken ct)
    {
        // Candidates query — read-only, no lease acquired here
        // tenantId obtained from ambient tenant context or per-tenant configuration
        // For system-level processing (all tenants), loop per tenant or use a multi-tenant query
        IReadOnlyList<OutboxMessage> candidates;
        await using (var scanScope = scopeFactory.CreateAsyncScope())
        {
            var scanStore = scanScope.ServiceProvider.GetRequiredService<IOutboxProcessorStore>();
            candidates = await scanStore.GetPendingAsync(_opts.BatchSize, tenantId: "*", ct)
                .ConfigureAwait(false);
        }

        // ✅ One scope per message — failure in one does not affect the others
        foreach (var candidate in candidates)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxProcessorStore>();
            var publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();

            await ProcessSingleAsync(store, publisher, candidate, ct).ConfigureAwait(false);
        }
    }

    private async Task ProcessSingleAsync(
        IOutboxProcessorStore store,
        IMessagePublisher publisher,
        OutboxMessage candidate,
        CancellationToken ct)
    {
        var lockExpiry = DateTimeOffset.UtcNow.AddMinutes(_opts.LockDurationInMinutes);

        // Atomic lease acquisition — returns false if another processor won
        var acquired = await store.AcquireLeaseAsync(candidate.Id, lockExpiry, ct).ConfigureAwait(false);
        if (!acquired)
        {
            logger.LogDebug("Lease already acquired by another processor for message {MessageId}", candidate.Id);
            return;
        }

        try
        {
            // TenantId is on the message — never read from IHttpContextAccessor
            logger.LogDebug("Publishing outbox message {MessageId} ({EventType}) for tenant {TenantId}",
                candidate.Id, candidate.EventType, candidate.TenantId);

            await publisher.PublishAsync(candidate, ct).ConfigureAwait(false);
            await store.MarkPublishedAsync(candidate.Id, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var nextRetryCount = candidate.RetryCount + 1;

            logger.LogWarning(ex,
                "Failed to publish outbox message {MessageId} (attempt {Attempt}/{MaxRetries})",
                candidate.Id, nextRetryCount, _opts.MaxRetries);

            if (nextRetryCount >= _opts.MaxRetries)
                await store.DeadLetterAsync(candidate.Id, ex.Message, ct).ConfigureAwait(false);
            else
                await store.MarkFailedAsync(candidate.Id, ex.Message, nextRetryCount, ct).ConfigureAwait(false);
        }
    }
}
```

> **Why scope-per-message:** A `DbContext` exception on message N corrupts the change tracker
> state for all subsequent messages in the same scope. A shared scope also means all messages
> in a batch share one `SaveChanges` boundary — a single failure rolls back the entire batch's
> state mutations. Per-message scopes guarantee isolation.

---

## Lease / Lock Pattern

The lease prevents two `OutboxProcessor` instances from processing the same message concurrently.

### EF Core atomic lease acquisition

```csharp
// ✅ EfOutboxProcessorStore.AcquireLeaseAsync — atomic via ExecuteUpdateAsync (single UPDATE WHERE)
public async ValueTask<bool> AcquireLeaseAsync(
    MessageId id, DateTimeOffset lockExpiry, CancellationToken ct = default)
{
    // Single UPDATE WHERE — atomic, no SELECT needed, safe under concurrent processors
    var rowsUpdated = await _ctx.OutboxMessages
        .Where(m => m.Id == id
                 && m.Status == OutboxMessageStatus.Pending
                 && (m.LockedUntilUtc == null || m.LockedUntilUtc < DateTimeOffset.UtcNow)
                 && (m.NextRetryAtUtc == null || m.NextRetryAtUtc <= DateTimeOffset.UtcNow))
        .ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Status, OutboxMessageStatus.Processing)
            .SetProperty(m => m.LockedUntilUtc, lockExpiry),
            ct)
        .ConfigureAwait(false);

    return rowsUpdated == 1;
}

// ❌ WRONG — SELECT + foreach mutation + SaveChanges is NOT atomic under concurrent processors
// Two processors can both SELECT the same Pending rows before either runs SaveChanges.
var messages = await _ctx.OutboxMessages.Where(m => m.Status == Pending).ToListAsync();
foreach (var m in messages)
    m.Status = Processing;  // ← mutation on init; property — won't compile with sealed record
await _ctx.SaveChangesAsync();  // ← not atomic: concurrent processor sees the same rows
```

### GetPendingAsync (candidates only — no lease)

```csharp
// ✅ Read-only candidate query — lease not acquired here
public async ValueTask<IReadOnlyList<OutboxMessage>> GetPendingAsync(
    int batchSize, string tenantId, CancellationToken ct = default)
{
    return await _ctx.OutboxMessages
        .Where(m => m.TenantId == tenantId        // always tenant-scoped
                 && m.Status == OutboxMessageStatus.Pending
                 && !m.DeadLettered
                 && (m.LockedUntilUtc == null || m.LockedUntilUtc < DateTimeOffset.UtcNow)
                 && (m.NextRetryAtUtc == null || m.NextRetryAtUtc <= DateTimeOffset.UtcNow))
        .OrderBy(m => m.CreatedAtUtc)
        .Take(batchSize)
        .AsNoTracking()                            // read-only — no change tracking needed
        .ToListAsync(ct)
        .ConfigureAwait(false);
}
```

### Stale lock recovery

Messages with `LockedUntilUtc < now` and `Status = Processing` are included in `GetPendingAsync`
via the `LockedUntilUtc < now` filter. Crashed processors release automatically on lock expiry.

---

## Retry Back-Off Formula

```csharp
// ✅ Exponential back-off with 1-hour cap
public static TimeSpan CalculateDelay(int retryCount)
    => TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, retryCount)));

// Examples:
// retryCount=0 → 1s
// retryCount=1 → 2s
// retryCount=2 → 4s
// retryCount=5 → 32s
// retryCount=10 → 1024s (~17 min)
// retryCount=11+ → 3600s (1 hour cap)
```

---

## Dead-Letter Pattern

```csharp
// When RetryCount + 1 >= MaxRetries:
await store.DeadLetterAsync(message.Id, "Max retries exceeded", ct);

// DeadLetterAsync sets:
//   Status = OutboxMessageStatus.Failed
//   DeadLettered = true
//   ProcessedAtUtc = UtcNow
//   ErrorMessage = reason
//
// Failed + DeadLettered=true is terminal — no further processing.
// Use GetDeadLetteredAsync + RequeueAsync for operator-driven reprocessing.
```

---

## Inbox Dedup — Full Flow

```csharp
// ✅ Complete idempotent inbox processing
public async ValueTask ProcessInboundAsync<T>(
    MessageEnvelope<T> envelope,
    IMessageHandler<T> handler,
    CancellationToken ct) where T : IIntegrationEvent
{
    // consumerType from the handler type — never from an existing inbox row
    var consumerType = handler.GetType().FullName!;

    // 1. Fast-path dedup check (optimization — not the sole guard)
    if (await _inboxStore.ExistsAsync(envelope.MessageId, consumerType, ct).ConfigureAwait(false))
    {
        _logger.LogDebug("Duplicate message {MessageId} for {Consumer} — skipping",
            envelope.MessageId, consumerType);
        return;
    }

    // 2. Record receipt — compound PK is the real concurrency guard
    try
    {
        await _inboxStore.AddAsync(
            InboxMessage.FromEnvelope(envelope, consumerType), ct).ConfigureAwait(false);
    }
    catch (DbUpdateException)
    {
        // Unique constraint violation — another processor won the race
        _logger.LogDebug("Concurrent duplicate for {MessageId}/{Consumer} — skipping",
            envelope.MessageId, consumerType);
        return;
    }

    // 3. Mark as Processing (acquire lease)
    await _inboxStore.MarkProcessingAsync(
        envelope.MessageId, consumerType, ct).ConfigureAwait(false);

    // 4. Invoke handler
    await handler.HandleAsync(envelope.Event, ct).ConfigureAwait(false);

    // 5. Mark as Processed
    await _inboxStore.MarkProcessedAsync(
        envelope.MessageId, consumerType, ct).ConfigureAwait(false);
}
```

---

## EF Core OutboxMessage Configuration

```csharp
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
               .HasConversion(id => id.Value, v => new MessageId(v));

        builder.Property(m => m.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(m => m.EventType).IsRequired().HasMaxLength(512);
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.Status)
               .HasConversion<string>()
               .HasMaxLength(32);
        builder.Property(m => m.CorrelationId)
               .HasConversion(id => id.Value, v => new CorrelationId(v));
        builder.Property(m => m.CausationId)
               .HasConversion(
                   id => id == null ? (Guid?)null : id.Value,
                   v => v == null ? null : new CausationId(v.Value));

        // Processor query index: Status + LockedUntilUtc + NextRetryAtUtc + TenantId
        builder.HasIndex(m => new { m.TenantId, m.Status, m.LockedUntilUtc, m.NextRetryAtUtc })
               .HasDatabaseName("ix_outbox_messages_tenant_status_lock_retry");

        // Cleanup query index
        builder.HasIndex(m => m.ProcessedAtUtc)
               .HasDatabaseName("ix_outbox_messages_processed_at");

        // Dead-letter query index
        builder.HasIndex(m => new { m.TenantId, m.DeadLettered })
               .HasDatabaseName("ix_outbox_messages_tenant_dead_letter");
    }
}
```

---

## InboxMessage Configuration

```csharp
public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");
        builder.HasKey(m => new { m.MessageId, m.ConsumerType }); // ← compound PK = dedup key

        builder.Property(m => m.MessageId)
               .HasConversion(id => id.Value, v => new MessageId(v));

        builder.Property(m => m.ConsumerType).IsRequired().HasMaxLength(512);
        builder.Property(m => m.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(m => m.EventType).IsRequired().HasMaxLength(512);
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.Status)
               .HasConversion<string>()
               .HasMaxLength(32);

        // Processor query index: TenantId + Status + LockedUntilUtc
        builder.HasIndex(m => new { m.TenantId, m.Status, m.LockedUntilUtc })
               .HasDatabaseName("ix_inbox_messages_tenant_status_lock");
    }
}
```
