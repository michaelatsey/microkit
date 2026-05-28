# Transaction Behavior Design

> **Status:** Planned — v2 (depends on MicroKit.Persistence + MicroKit.Messaging)
> **Scope:** This document captures the full architectural design for `TransactionBehavior`
> to be implemented once `MicroKit.Persistence` and `MicroKit.Messaging` are available.
> Load this file when working on TransactionBehavior, MicroKit.Persistence, or MicroKit.Messaging.

---

## Architecture Overview

The system has **5 distinct levels** with clearly separated scopes:

| Level | Component | Scope | Consistency |
|-------|-----------|-------|-------------|
| 1 | DomainEvent | Business transaction | Synchronous, atomic |
| 2 | DomainEventHandler | Intra-domain consistency | Same DbContext, same transaction |
| 3 | DomainEventNotification + Outbox | Deferred reaction | Eventually consistent |
| 4 | NotificationHandler + IntegrationMessage | Application orchestration | Async, separate transaction |
| 5 | BrokerWorker | External distributed messaging | At-least-once delivery |

---

## Sequence Diagrams

### 1. Command Flow — Transaction Scope

The most important diagram. Shows the full transactional boundary:
- Command Handler execution
- Aggregate mutation
- DomainEvent raise
- DomainEventHandler modifying another aggregate **in the same transaction**
- OutboxMessage persisted atomically
- Single SaveChanges + single commit

```mermaid
sequenceDiagram
    autonumber

    actor Client

    participant MR as MediatR
    participant TB as TransactionBehavior
    participant CH as CommandHandler

    participant OA as OrderAggregate
    participant DE as DomainEventsProvider

    participant DH as DomainEventHandler

    participant CA as CustomerAggregate

    participant NM as NotificationMapper
    participant OB as OutboxStore

    participant UOW as UnitOfWork
    participant DB as Database

    rect rgb(235, 245, 255)
        Note over TB,DB: TRANSACTION SCOPE

        Client->>MR: Send(CreateOrderCommand)
        MR->>TB: Handle(command)
        TB->>CH: Execute command
        CH->>OA: Create Order
        OA-->>DE: Raise OrderPlacedDomainEvent

        TB->>DE: Get domain events

        loop For each DomainEvent
            TB->>DH: Dispatch DomainEvent
            Note over DH: Pure transactional domain logic
            DH->>CA: Update Customer loyalty points
            CA-->>DE: Raise CustomerLoyaltyUpdatedDomainEvent
            Note over DH: Optional notification mapping
            DH->>NM: Resolve matching notifications
            NM->>OB: Create OutboxMessage
        end

        Note over TB: Drain recursively raised events

        TB->>UOW: SaveChangesAsync()
        UOW->>DB: Persist aggregates + outbox messages
        DB-->>UOW: Commit transaction
    end

    TB-->>MR: Return response
    MR-->>Client: Success
```

---

### 2. Recursive Domain Event Draining Flow

Critical: `DomainEventHandler` instances can raise new events during dispatch.
The drain loop must continue until the queue is empty.

```mermaid
sequenceDiagram
    autonumber

    participant TB as TransactionBehavior
    participant Q as DomainEventQueue

    participant H1 as OrderPlacedHandler
    participant H2 as LoyaltyHandler
    participant H3 as InvoiceHandler

    rect rgb(235, 245, 255)
        Note over TB,H3: SAME TRANSACTION SCOPE

        TB->>Q: Enqueue(OrderPlacedDomainEvent)

        loop Until queue empty
            Q-->>TB: Dequeue next event
            TB->>H1: Handle(OrderPlacedDomainEvent)
            H1->>Q: Enqueue(CustomerPointsGrantedDomainEvent)
            H1->>Q: Enqueue(InvoiceRequestedDomainEvent)
            Q-->>TB: Dequeue(CustomerPointsGrantedDomainEvent)
            TB->>H2: Handle(CustomerPointsGrantedDomainEvent)
            Q-->>TB: Dequeue(InvoiceRequestedDomainEvent)
            TB->>H3: Handle(InvoiceRequestedDomainEvent)
        end
    end
```

---

### 3. Outbox Processing Flow

Outside the business transaction scope — async processing.

```mermaid
sequenceDiagram
    autonumber

    participant W as OutboxWorker
    participant DB as Database
    participant DES as DomainEventSerializer
    participant MR as MediatR
    participant NH as NotificationHandler

    rect rgb(255, 248, 235)
        Note over W,NH: ASYNCHRONOUS PROCESSING SCOPE

        W->>DB: Read pending OutboxMessages
        DB-->>W: Serialized notifications

        loop For each message
            W->>DES: Deserialize notification
            DES-->>W: DomainEventNotification<T>
            W->>MR: Publish(notification)
            MR->>NH: Execute NotificationHandler
            NH-->>MR: Completed
            W->>DB: Mark message as processed
        end
    end
```

---

### 4. Integration Message Flow

```mermaid
sequenceDiagram
    autonumber

    participant NH as NotificationHandler
    participant IQ as IntegrationQueueStore
    participant UOW as UnitOfWork
    participant DB as Database

    rect rgb(255, 248, 235)
        Note over NH,DB: APPLICATION INTEGRATION SCOPE

        NH->>IQ: Create IntegrationMessage
        NH->>UOW: SaveChangesAsync()
        UOW->>DB: Persist IntegrationMessage
        DB-->>UOW: Commit
    end
```

---

### 5. Broker Publishing Flow

```mermaid
sequenceDiagram
    autonumber

    participant W as IntegrationWorker
    participant DB as Database
    participant BR as Kafka/RabbitMQ

    rect rgb(255, 240, 240)
        Note over W,BR: EXTERNAL DISTRIBUTED MESSAGING

        W->>DB: Read IntegrationMessages
        DB-->>W: Pending messages

        loop For each integration message
            W->>BR: Publish message
            BR-->>W: Ack
            W->>DB: Mark as published
        end
    end
```

---

### 6. Failure / Retry / DeadLetter Flow

```mermaid
sequenceDiagram
    autonumber

    participant W as Worker
    participant DB as Database
    participant BR as Broker

    loop Retry Policy
        W->>DB: Read pending message
        W->>BR: Publish

        alt Success
            BR-->>W: Ack
            W->>DB: Mark processed
        else Failure
            BR-->>W: Error
            W->>DB: Increment retry count
        end
    end

    alt Retry limit exceeded
        W->>DB: Move to DeadLetter
    end
```

---

## TransactionBehavior Implementation Design

### Dependencies

```csharp
// Required — from MicroKit.Persistence (not yet implemented)
ITransactionalContext  // wraps the DB transaction
IUnitOfWork            // SaveChangesAsync()

// Required — from MicroKit.MediatR (already available)
IDomainEventDispatcher // dispatch events

// Required — from MicroKit.Domain (already available)
IDomainEventsProvider  // collect events raised during dispatch
```

### Decorator Pattern — IDomainEventsProvider

`IDomainEventsProvider` is implemented as a **decorator** on the DbContext or repositories.
It automatically captures domain events raised by **any** aggregate modified during the transaction —
including aggregates modified by secondary `DomainEventHandler` instances.

This means `IDomainEventsProvider` **fills itself** during `DispatchAsync` — the drain loop
in `TransactionBehavior` picks up newly raised events automatically on the next iteration.

### Correct Recursive Drain Loop

```csharp
private static async ValueTask InvokeCommandAsync(TransactionState state, CancellationToken ct)
{
    // Execute the handler
    state.Response = await state.Next(ct).ConfigureAwait(false);

    // Recursive drain — handlers can raise new events during dispatch
    while (true)
    {
        var events = state.DomainEventsProvider.GetAllDomainEvents();
        if (events.Count == 0) break;

        // Clear BEFORE dispatch — so new events raised during dispatch are captured
        state.DomainEventsProvider.ClearAllDomainEvents();
        await state.Dispatcher.DispatchAsync(events, ct).ConfigureAwait(false);
        // Loop will pick up any newly raised events on next iteration
    }

    // Single SaveChanges — all aggregates + outbox messages persisted atomically
    await state.UnitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
}
```

### v1 Reference Implementation (annotated)

The following is the v1 draft with corrections applied:

```csharp
/// <summary>
/// Pipeline behavior that wraps command execution in a database transaction,
/// dispatches domain events recursively within the transaction boundary,
/// and persists all changes atomically via <see cref="IUnitOfWork"/>.
/// Query requests bypass the transaction entirely.
/// </summary>
/// <remarks>
/// Requires MicroKit.Persistence (ITransactionalContext, IUnitOfWork) and
/// MicroKit.Messaging (IOutboxStore via DomainEventHandler).
/// PipelineOrder: <see cref="PipelineOrder.Transaction"/> (700 — after Retry).
/// </remarks>
public sealed class TransactionBehavior<TRequest, TResponse>(
    ITransactionalContext transactionalContext,
    IDomainEventsProvider domainEventsProvider,
    IDomainEventDispatcher domainEventDispatcher,
    IUnitOfWork unitOfWork)
    : BehaviorBase<TRequest, TResponse>           // ✅ BehaviorBase, not IPipelineBehavior
    where TRequest : IRequest<TResponse>
{
    public override int Order => PipelineOrder.Transaction; // 700

    public override async ValueTask<TResponse> Handle( // ✅ ValueTask, not Task
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Only wrap commands — queries are read-only, bypass transaction
        if (request is not ICommand)
            return await next().ConfigureAwait(false);

        var state = new TransactionState(next, domainEventsProvider, domainEventDispatcher, unitOfWork);

        await transactionalContext.ExecuteAsync(
            static async (st, ct) => await InvokeCommandAsync(st, ct).ConfigureAwait(false),
            state,
            cancellationToken).ConfigureAwait(false);

        return state.Response!;
    }

    private static async ValueTask InvokeCommandAsync(TransactionState state, CancellationToken ct)
    {
        state.Response = await state.Next().ConfigureAwait(false);

        // Recursive domain event drain
        while (true)
        {
            var events = state.DomainEventsProvider.GetAllDomainEvents();
            if (events.Count == 0) break;
            state.DomainEventsProvider.ClearAllDomainEvents();
            await state.Dispatcher.DispatchAsync(events, ct).ConfigureAwait(false);
        }

        await state.UnitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // Closure-free state carrier — avoids heap allocation per pipeline invocation
    private sealed class TransactionState(
        RequestHandlerDelegate<TResponse> next,
        IDomainEventsProvider domainEventsProvider,
        IDomainEventDispatcher dispatcher,
        IUnitOfWork unitOfWork)
    {
        public RequestHandlerDelegate<TResponse> Next { get; } = next;
        public IDomainEventsProvider DomainEventsProvider { get; } = domainEventsProvider;
        public IDomainEventDispatcher Dispatcher { get; } = dispatcher;
        public IUnitOfWork UnitOfWork { get; } = unitOfWork;
        public TResponse? Response { get; set; }
    }
}
```

### PipelineOrder for TransactionBehavior

```csharp
// Add to PipelineOrder.cs when implementing:
public const int Transaction = 700; // After Retry(600), wraps the full command execution
```

### Open Questions for v2 Implementation

1. **Infinite loop guard** — what if a DomainEventHandler always raises a new event?
   Add a `maxDrainIterations` limit (default: 10) with a clear exception message.

2. **Partial failure** — if `DispatchAsync` fails mid-drain, some events were dispatched
   and some weren't. The transaction rollback handles DB state, but in-memory state
   of `IDomainEventsProvider` may be inconsistent. Needs investigation.

3. **`ITransactionalContext` contract** — to be defined in `MicroKit.Persistence.Abstractions`.
   Must support `ExecuteAsync<TState>(Func<TState, CancellationToken, ValueTask>, TState, CancellationToken)`.

---

## Scope Visualization

```
🔵 BLUE  = Business transaction (synchronous, atomic)
🟠 ORANGE = Async application processing (eventually consistent)
🔴 RED   = External distributed messaging (at-least-once)

Level 1: DomainEvent              🔵 Business transaction
Level 2: DomainEventHandler       🔵 Intra-domain consistency (same DbContext)
Level 3: DomainEventNotification  🔵→🟠 Bridge (Outbox persisted in transaction, processed async)
Level 4: NotificationHandler      🟠 Application orchestration
Level 5: IntegrationMessage       🟠→🔴 Bridge (persisted, then published)
Level 6: BrokerWorker             🔴 External transport
```
