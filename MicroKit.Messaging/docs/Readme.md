# MicroKit.Messaging

Outbox/Inbox pattern for .NET 10. Atomic enqueue, background delivery, lock-free concurrent workers, per-consumer state tracking — the implementation details that naive outbox libraries skip.

---

## What makes this production-grade

**Envelope wrapping before serialization.** `OutboxService.EnqueueAsync<T>` does not serialize the payload directly. It first wraps it in `EventEnvelope<T>` — which carries `TenantId`, `MessageType`, `OccurredOnUtc`, `CorrelationId`, `CausationId`, `IdempotencyKey`, and `Metadata` — then serializes the envelope. The consuming side deserializes the envelope, recovers all tracing context, and then deserializes the payload. This pattern ensures that no correlation metadata is lost in transit even if the broker strips headers.

**Destination validation at enqueue time.** `OutboxService.ValidateMessage` throws `ArgumentException` if no destination channel is enabled (`PublishAsNotification=false` and `PublishToBroker=false`), and throws again if `PublishToBroker=true` but `BrokerTopic` is empty. These checks fail at the call site — not silently hours later when a worker tries to route the message.

**Three-phase optimistic inbox locking — no `SELECT FOR UPDATE`.** `OptimisticInboxLockingStrategy.LockNextAsync` runs three steps: (1) `AsNoTracking` read of candidate IDs ordered by `OccurredOnUtc` (FIFO), (2) bulk `ExecuteUpdateAsync` with a re-check predicate that only updates records still in `Pending` or `Failed` state with no live lock, (3) re-fetch of only the records whose `LockedUntilUtc` matches the lock window the worker set. If a concurrent worker already locked some candidates between steps 1 and 2, `affectedRows` drops and the fetch returns only what this worker actually owns. No serializable isolation, no advisory locks.

**Per-consumer state tracking separate from message storage.** `InboxMessage` stores the raw message once. `InboxState` is a separate row per (message, consumer) pair. The same message can be processed by ten different handler types at different rates and retry counts without any interference. `AttemptCount`, `NextAttemptAtUtc`, `LockedBy`, and `LastError` are tracked independently for each consumer.

**Scheduled delivery and backoff.** `OutboxMessage.ScheduledAtUtc` delays delivery until a future time. `InboxState.NextAttemptAtUtc` enables exponential backoff between retries — the locking strategy only picks up records where `NextAttemptAtUtc <= now`. Both fields coexist with the `LockedUntilUtc` distributed lock window.

**Dual publication channel.** A single outbox message can be both an in-process MediatR notification (`PublishAsNotification = true`) and a broker event (`PublishToBroker = true`) with a `BrokerTopic` and `PartitionKey`. This allows the same write to fan out to local handlers and external consumers atomically.

**Batch enqueue materializes in one pass.** `EnqueueBatchAsync` calls `.ToList()` on the prepared messages before handing them to the repository, so the EF Core ChangeTracker receives the full batch in a single `AddRangeAsync` — one round trip, not N inserts.

---

## Installation

```shell
# Contracts — IOutboxService, IInboxHandler<T>, IMessageTransport, message types
dotnet add package MicroKit.Messaging.Abstractions

# Core workers, UseOutbox(), UseInbox(), MessagingValidationService
dotnet add package MicroKit.Messaging.Core

# EF Core repositories, 3-phase optimistic locking, SQL Server / PostgreSQL configurations
dotnet add package MicroKit.Messaging.Persistence.EFCore

# RabbitMQ IMessageTransport implementation (optional)
dotnet add package MicroKit.Messaging.Transport.RabbitMQ
```

---

## Usage

### Enqueue an outbox message (atomic with the business transaction)

```csharp
using MicroKit.Messaging.Abstractions.Outbox;

public sealed class ConfirmOrderHandler : ICommandHandler<ConfirmOrderCommand, Result>
{
    private readonly IRepository<Order> _orders;
    private readonly IUnitOfWork _uow;
    private readonly IOutboxService _outbox;
    private readonly ITenantContext _tenant;

    public ConfirmOrderHandler(
        IRepository<Order> orders,
        IUnitOfWork uow,
        IOutboxService outbox,
        ITenantContext tenant)
        => (_orders, _uow, _outbox, _tenant) = (orders, uow, outbox, tenant);

    public async Task<Result> HandleAsync(ConfirmOrderCommand cmd, CancellationToken ct)
    {
        var order = await _orders.FindByIdAsync(cmd.OrderId, ct);
        if (order is null) return Result.Failure(OrderErrors.NotFound);

        order.Confirm();
        _orders.Update(order);

        // EnqueueAsync stages the OutboxMessage in the same DbContext ChangeTracker.
        // IUnitOfWork.SaveChangesAsync commits the order update and the outbox row atomically.
        await _outbox.EnqueueAsync(
            tenantId: _tenant.Tenant!.Id,
            messageId: Guid.NewGuid().ToString(),
            payload: new OrderConfirmedIntegrationEvent(order.Id, order.CustomerId),
            destination: new OutboxDestination
            {
                PublishToBroker = true,
                BrokerTopic = "orders.confirmed",
                PartitionKey = order.CustomerId   // ensures ordered delivery per customer
            },
            correlationId: cmd.CorrelationId,
            cancellationToken: ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

Internally, `EnqueueAsync<T>` wraps `OrderConfirmedIntegrationEvent` in `EventEnvelope<T>` before serializing. The payload column contains the full envelope JSON including tenant and tracing metadata.

### Enqueue a dual-channel message (in-process + broker)

```csharp
await _outbox.EnqueueAsync(
    tenantId: tenantId,
    messageId: messageId,
    payload: new OrderShippedEvent(orderId, trackingNumber),
    destination: new OutboxDestination
    {
        PublishAsNotification = true,  // dispatched as MediatR INotification in-process
        PublishToBroker = true,
        BrokerTopic = "orders.shipped"
    },
    cancellationToken: ct);
```

Both channels receive the message from the same outbox row. The `OutboxPublisherWorker` handles routing.

### Implement an inbox handler

```csharp
using MicroKit.Messaging.Abstractions.Inbox;

public sealed class OrderConfirmedHandler : IInboxHandler<OrderConfirmedIntegrationEvent>
{
    private readonly IStockService _stock;
    private readonly ITenantContext _tenant;

    public OrderConfirmedHandler(IStockService stock, ITenantContext tenant) =>
        (_stock, _tenant) = (stock, tenant);

    public async Task HandleAsync(OrderConfirmedIntegrationEvent message, CancellationToken ct)
    {
        // _tenant is already resolved by the InboxPublisherWorker before this call
        await _stock.ReserveAsync(message.OrderId, ct);
    }
}
```

`UseInbox()` scans the specified assemblies for all `IInboxHandler<T>` implementations and registers them in DI automatically. No explicit registration per handler.

### Batch enqueue

```csharp
var messages = orders.Select(o => new OutboxMessage
{
    Id = Guid.NewGuid().ToString(),
    TenantId = tenantId,
    MessageType = typeof(OrderExportedEvent).FullName!,
    Payload = _serializer.Serialize(new EventEnvelope<OrderExportedEvent> { ... }),
    PublishToBroker = true,
    BrokerTopic = "orders.exported",
    Status = MessageStatus.Pending
}).ToList();

await _outbox.EnqueueBatchAsync(messages, ct);
await _uow.SaveChangesAsync(ct);
```

---

## Configuration

```csharp
using MicroKit.Messaging.Core.Extensions;
using MicroKit.Messaging.Core.Extensions.Outbox;
using MicroKit.Messaging.Core.Extensions.Inbox;

builder.Services
    .AddMicroKit()
    .AddMicroKitMessaging(messaging =>
    {
        messaging.UseOutbox(outbox =>
        {
            outbox.BatchSize             = 100;
            outbox.PollingInterval       = TimeSpan.FromSeconds(5);
            outbox.MaxRetryCount         = 5;
            outbox.UseExponentialBackoff = true;
            outbox.RetryDelay            = TimeSpan.FromSeconds(30);
            outbox.MessageExpiration     = TimeSpan.FromDays(7);
            outbox.RetentionPeriod       = TimeSpan.FromDays(7);
            outbox.FailedRetentionPeriod = TimeSpan.FromDays(30);
            outbox.CleanupEnabled        = true;
            outbox.CleanupRunInterval    = TimeSpan.FromHours(1);
        });

        messaging.UseInbox(
            configure: inbox =>
            {
                inbox.BatchSize       = 50;
                inbox.PollingInterval = TimeSpan.FromSeconds(3);
            },
            assembliesToScan: typeof(OrderConfirmedHandler).Assembly);

        messaging.AddRepositories<
            EfOutboxRepository<AppDbContext>,
            EfInboxMessageRepository<AppDbContext>>();
    });
```

### appsettings.json

All options bind from `MicroKit:Messaging:Outbox` and `MicroKit:Messaging:Inbox`:

```json
{
  "MicroKit": {
    "Messaging": {
      "Outbox": {
        "Enabled": true,
        "BatchSize": 100,
        "PollingInterval": "00:00:05",
        "MaxRetryCount": 5,
        "UseExponentialBackoff": true,
        "MessageExpiration": "7.00:00:00",
        "RetentionPeriod": "7.00:00:00"
      },
      "Inbox": {
        "Enabled": true,
        "BatchSize": 50,
        "PollingInterval": "00:00:03"
      }
    }
  }
}
```

### Add messaging tables to your DbContext

```csharp
using MicroKit.Messaging.Persistence.EFCore.Extensions;

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    // Applies OutboxMessages and InboxMessages / InboxStates table configurations
    modelBuilder.ApplyMessagingConfiguration(DatabaseProvider.SqlServer);
    // or: DatabaseProvider.PostgreSQL
}
```

---

## Key types

| Type | Package | Role |
|---|---|---|
| `IOutboxService` | Abstractions | Enqueue messages into the outbox table |
| `IInboxHandler<T>` | Abstractions | Implement to handle a specific message type |
| `OutboxMessage` | Abstractions | Persistent outbox row with status, retry, lock fields |
| `InboxMessage` | Abstractions | Immutable incoming message (written once) |
| `InboxState` | Abstractions | Per-consumer mutable processing state |
| `OutboxDestination` | Abstractions | Sealed init-only routing descriptor |
| `MessageStatus` | Abstractions | `Pending`, `Processing`, `Completed`, `Failed` |
| `OutboxPublisherWorker` | Core | Background worker — polls, locks, dispatches |
| `InboxPublisherWorker` | Core | Background worker — polls, locks, dispatches to handlers |
| `OutboxCleanupWorker` | Core | Removes expired completed/failed messages |
| `InboxCleanupWorker` | Core | Removes expired processed states |
| `OptimisticInboxLockingStrategy` | Persistence.EFCore | 3-phase lock-free concurrent handler |
| `MessagingValidationService` | Core | Startup-time dependency validation |

---

## Package dependency graph

```
MicroKit.Messaging.Abstractions
    MicroKit.Abstractions
    (no third-party NuGet dependencies)

MicroKit.Messaging.Core
    MicroKit.Messaging.Abstractions
    MicroKit.Abstractions
    Microsoft.Extensions.Hosting

MicroKit.Messaging.Persistence.EFCore
    MicroKit.Messaging.Abstractions
    MicroKit.Messaging.Core
    Microsoft.EntityFrameworkCore

MicroKit.Messaging.Transport.RabbitMQ
    MicroKit.Messaging.Abstractions
    RabbitMQ.Client
```
