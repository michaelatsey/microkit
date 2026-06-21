# microkit-messaging-testing

## Libraries

| Library | Role | Status |
|---------|------|--------|
| `xUnit` | Test runner | ✅ Required |
| `Shouldly` | Assertions | ✅ Required |
| `NSubstitute` | Mocking | ✅ Required |
| `NetArchTest` | Architecture tests | ✅ Required |
| `FluentAssertions` | — | ❌ Banned (Xceed commercial licence) |
| `MediatR.Contracts` | — | ❌ Banned everywhere EXCEPT the `MicroKit.Messaging.MediatR` glue (ADR-MSG-009 carve-out) |

---

## MicroKit.Messaging.Testing Usage

Always use the provided test doubles — never instantiate EF stores directly in unit tests.

```csharp
// ✅ FakeMessagePublisher — records published messages for assertions
var publisher = new FakeMessagePublisher();
await sut.HandleAsync(command, ct);

publisher.ShouldHavePublished<OrderPlacedEvent>();
publisher.ShouldHavePublished<OrderPlacedEvent>(e => e.OrderId == orderId);

// ✅ InMemoryOutboxStore — implements IOutboxWriter + IOutboxProcessorStore
// Use as IOutboxWriter in domain handler tests; as IOutboxProcessorStore in processor tests
var outboxStore = new InMemoryOutboxStore();
await sut.CommitAsync(ct);

var pending = await outboxStore.GetPendingAsync(batchSize: 10, ct);
pending.Count.ShouldBe(1);
pending[0].TenantId.ShouldBe("tenant-abc"); // TenantId is on the row, not a filter parameter

// ✅ InMemoryInboxStore — in-memory inbox for dedup tests
var inboxStore = new InMemoryInboxStore();
var alreadyProcessed = await inboxStore.ExistsAsync(messageId, consumerType, ct);
alreadyProcessed.ShouldBeFalse();

await inboxStore.AddAsync(inboxMessage, ct);
var nowExists = await inboxStore.ExistsAsync(messageId, consumerType, ct);
nowExists.ShouldBeTrue();
```

---

## Test Categories

### Unit Tests (`MicroKit.Messaging.UnitTests`)
- `IMessagePublisher` dispatch logic (happy path, null publisher, cancellation)
- `OutboxProcessor` state transitions (Pending → Processing → Published/Failed)
- `InboxProcessor` dedup gate (ExistsAsync gate prevents double-processing)
- `OutboxMessage` retry back-off formula verification
- `InboxMessage` compound dedup key isolation
- `MessageEnvelope<T>` CorrelationId/CausationId chain propagation
- `IOutboxStore` / `IInboxStore` contracts via `InMemoryOutboxStore` / `InMemoryInboxStore`

### Integration Tests (`MicroKit.Messaging.IntegrationTests`)
- Full outbox → dispatch → inbox cycle with EF Core (SQLite in-memory)
- Lease/lock acquisition preventing double-processing (two concurrent OutboxProcessors)
- Dead-letter flow (MaxRetries exceeded)
- Tenant isolation (messages from TenantA not visible to TenantB queries)
- CorrelationId preserved end-to-end

### Architecture Tests (`MicroKit.Messaging.ArchitectureTests`)
- Abstractions has zero ASP.NET Core / EF Core / broker dependency
- Core has zero EF Core / broker dependency
- Testing package has zero Core / EF Core dependency (Abstractions only)
- `MediatR.Contracts` absent from all assemblies EXCEPT the `MicroKit.Messaging.MediatR` glue (ADR-MSG-009)
- Broker provider packages do not depend on each other
- No circular dependencies

### Performance Tests (`MicroKit.Messaging.PerformanceTests`)
- `OutboxStore.AddAsync` allocation benchmark
- `OutboxProcessor.GetPendingAsync` batch retrieval overhead
- `InboxStore.ExistsAsync` dedup check latency

---

## Mandatory Test Cases Per Component

### OutboxProcessor
```
ProcessBatch_WhenMessagesPending_AcquiresLeaseAndPublishes
ProcessBatch_WhenPublisherFails_ResetsStatusToPending_IncrementsRetry
ProcessBatch_WhenMaxRetriesExceeded_SetsFailedAndDeadLettered
ProcessBatch_WhenNoMessages_DoesNothing
ProcessBatch_WhenCancelled_StopsGracefully
AcquireLeaseAsync_WhenSameMessage_ReturnsFalseForSecondAcquirer  (unit — optimistic lock)
```

### Integration Tests (OutboxProcessor)
```
ProcessBatch_WhenTwoProcessorsConcurrent_EachMessageProcessedOnce  (lease isolation — requires real DB)
```

### InboxProcessor / Dedup Gate
```
ProcessMessage_WhenAlreadyProcessed_SkipsHandler
ProcessMessage_WhenNotProcessed_InvokesHandler
ProcessMessage_WhenHandlerThrows_MarksFailedAndRetains
ProcessMessage_WhenSameMessageDifferentConsumer_ProcessesBoth  (compound key)
```

### IOutboxWriter + IOutboxProcessorStore (InMemoryOutboxStore)
```
AddAsync_StoresMessage_WithTenantId
GetPendingAsync_OnlyReturnsPendingMessages
GetPendingAsync_RespectsLease_DoesNotReturnLockedMessages
GetPendingAsync_FiltersExpiredLocks_ReturnsStaleLockedMessages
GetPendingAsync_ReturnsAllTenants_TenantIdOnEachRow
AcquireLeaseAsync_WhenPending_ReturnsTrue
AcquireLeaseAsync_WhenAlreadyLocked_ReturnsFalse
MarkPublishedAsync_UpdatesStatusToPublished
MarkFailedAsync_ResetsStatusToPending_IncrementsRetryCount_SetsNextRetryAt
DeadLetterAsync_SetStatusFailed_SetsDeadLetteredTrue
```

### IInboxStore (InMemoryInboxStore)
```
ExistsAsync_WhenNotPresent_ReturnsFalse
ExistsAsync_WhenPresent_ReturnsTrue
AddAsync_WithSameMessageIdDifferentConsumer_StoresBothRows
MarkProcessedAsync_UpdatesStatus
```

### FakeMessagePublisher
```
ShouldHavePublished_WhenEventPublished_Passes
ShouldHavePublished_WhenNotPublished_Fails
ShouldHavePublished_WithPredicate_FiltersCorrectly
PublishedMessages_ClearedBetweenTests
```

---

## Architecture Test Pattern

```csharp
[Fact]
public void Abstractions_ShouldHave_ZeroEfCoreDependency()
{
    Types.InAssembly(typeof(IIntegrationEvent).Assembly)
        .ShouldNot()
        .HaveDependencyOn("Microsoft.EntityFrameworkCore")
        .GetResult()
        .IsSuccessful
        .ShouldBeTrue();
}

[Fact]
public void AllAssemblies_ShouldNot_ReferenceMediatRContracts()
{
    // ADR-MSG-009: the MicroKit.Messaging.MediatR glue is intentionally EXCLUDED — it is the
    // single package permitted to reference MediatR / MediatR.Contracts.
    var assemblies = new[]
    {
        typeof(IIntegrationEvent).Assembly,       // Abstractions
        typeof(InProcessMessagePublisher).Assembly, // Core
        typeof(EfOutboxStore).Assembly,            // EntityFrameworkCore
        typeof(FakeMessagePublisher).Assembly,     // Testing (when implemented)
    };

    foreach (var assembly in assemblies)
    {
        Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOn("MediatR.Contracts")
            .GetResult()
            .IsSuccessful
            .ShouldBeTrue($"{assembly.GetName().Name} must not reference MediatR.Contracts");
    }
}
```

---

## Naming Convention

```
{Method}_{Scenario}_{ExpectedResult}

✅ PublishAsync_WhenPublisherNull_ThrowsInvalidOperation
✅ ExistsAsync_WhenAlreadyProcessed_ReturnsTrue
✅ ProcessBatch_WhenMaxRetriesExceeded_DeadLettersMessage
❌ TestOutboxProcessor
❌ ShouldPublishMessage
```

---

## Rules

1. **No `MediatR.Contracts`** in any test project `.csproj` — zero tolerance. (The glue's own test
   project, `MicroKit.Messaging.MediatR.UnitTests`, transitively references MediatR via the glue
   under test — that is the ADR-MSG-009 carve-out, not a violation.)
2. **Fresh test double per test** — `FakeMessagePublisher`, `InMemoryOutboxStore`, `InMemoryInboxStore` never shared
3. **`TenantId` always set** in test fixtures — never null or empty string
4. **SQLite isolation** — integration tests: each `Task.Run` must use its own isolated connection
5. **No `Thread.Sleep` in tests** — use `Task.Delay` with `CancellationToken` if timing matters
6. **`ConfigureAwait(false)` in test helpers** — not in test methods themselves
7. **`GenerateDocumentationFile=false`** in all test `.csproj` files
8. **`NoWarn CS1591;CA1707`** in all test `.csproj` files
