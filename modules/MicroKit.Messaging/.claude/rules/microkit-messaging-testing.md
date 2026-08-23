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

Always use the provided test doubles in UNIT tests — never instantiate EF stores there. Inbox
store behaviour is asserted in the integration suite against a real provider instead.

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

// ✅ Inbox ingestion returns a result — a redelivery is reported, never thrown (ADR-MSG-017)
var result = await inboxWriter.AddAsync(inboxMessage, ct);
result.ShouldBe(InboxWriteResult.Added);

var redelivered = await inboxWriter.AddAsync(Clone(inboxMessage), ct);
redelivered.ShouldBe(InboxWriteResult.AlreadyPresent);   // the nominal path, not an error
```

---

## Test Categories

### Unit Tests (`MicroKit.Messaging.UnitTests`)
- `IMessagePublisher` dispatch logic (happy path, null publisher, cancellation)
- `OutboxProcessor` state transitions (Pending → Processing → Published/Failed)
- `InboxProcessor` claim, settlement and failure classification (drives `FakeTimeProvider`)
- `OutboxMessage` retry back-off formula verification
- `InboxMessage` compound dedup key isolation (unique index; the PK is the `RowId` surrogate)
- `MessageEnvelope<T>` CorrelationId/CausationId chain propagation
- Inbox claim/settlement contracts via `EfInboxStore` on SQLite (the claim carries no
  provider-specific SQL, so the production path is what runs)

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

### InboxProcessor (claim / settlement — ADR-MSG-017)
```
ProcessBatch_WhenNothingClaimable_ReturnsEmptyAndSettlesNothing
ProcessBatch_WhenHandlerCommitsTheStagedMark_WritesNoOutcome   (success settles itself)
ProcessBatch_StagesTheMarkBeforeInvokingTheHandler
ProcessBatch_WhenLeaseLostBeforeHandler_CountsItAndLeavesTheRowAlone
ProcessBatch_WhenLeaseLostDuringHandler_CountsItAndConsumesNoRetry
ProcessBatch_WhenADomainConflictIsNotALostLease_KeepsItsRetry
ProcessBatch_WhenConsumerNotRegistered_DeadLettersOnFirstSight
ProcessBatch_WhenPayloadDoesNotDeserialize_DeadLettersOnFirstSight
ProcessBatch_WhenHandlerThrows_BelowMaxRetries_SchedulesARetry
ProcessBatch_WhenHandlerThrows_AtMaxRetries_DeadLetters
ProcessBatch_WhenHandlerCommitsNothing_WritesADeferredProcessedOutcome
ProcessBatch_WhenHandlerCommitsThenThrows_CountsItProcessedAndDoesNotRetry
ProcessBatch_WhenDependencyUnavailable_ReleasesTheRemainderWithoutRetries
ProcessBatch_WhenSettlementStoreMissing_SettlesReleasedThenRethrows
ProcessBatch_WhenCancelled_ReleasesEveryUnattemptedRow
ComputeBackoffCeiling_FollowsTheExponentialCurve                (exact values, FixedRandom)
```

### Inbox ingestion (the dedup gate)
```
First_delivery_inserts_the_row
Redelivery_is_reported_as_already_present_and_does_not_throw
Context_remains_usable_after_a_deduplicated_write
A_duplicate_for_one_consumer_does_not_block_the_others
A_non_duplicate_persistence_failure_still_throws                (NOT NULL, every provider)
The_unique_index_is_what_rejects_the_duplicate
```

> `A_non_duplicate_persistence_failure_still_throws` uses a **NOT NULL** violation deliberately.
> A max-length overflow is the obvious alternative and is wrong: SQLite does not enforce length
> constraints, so the insert would succeed and the one test standing between the dedup fix and
> silent data loss would not run on the provider the fast suite uses.

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

### Inbox stores (EfInboxStore, SQLite)
```
ClaimBatchAsync_StampsStatusLeaseAndToken
ClaimBatchAsync_RecoversAnExpiredLease
ClaimBatchAsync_DoesNotStealALiveLease
ClaimBatchAsync_NeverExceedsBatchSize                    (the cross-product bound)
ClaimBatchAsync_TwoProcessorsNeverWinTheSameRow
ApplyOutcomesAsync_WithTheOwningToken_WritesEveryDisposition
ApplyOutcomesAsync_WithAStaleToken_WritesNothing         (the lost update)
ApplyOutcomesAsync_DoesNotTouchASiblingConsumersRow
StageProcessedAsync_StagesWithoutCommitting
StageProcessedAsync_WithAStaleToken_ReturnsFalse
GetDeadLetteredAsync_WithNullTenant_MatchesRowsThatHaveNoTenant
DeleteProcessedAsync_WithNullTenant_DeletesRowsThatHaveNoTenant
```

### Inbox — PostgreSQL only (`[DockerRequiredFact]`, `PostgreSqlSuite`)

Two tests decide whether the design is correct, and neither can be replaced by reading code:
```
InboxLeaseExpiryTests.WhenTheLeaseExpiresMidHandler_TheLoserRollsBackEntirelyAndTheWinnerStands
InboxClaimConcurrencyTests.ClaimBatchAsync_TwoProcessorsOverAFannedOutQueue_AreDisjointAndBounded
```
> The first fails if `ClaimToken` is not mapped as an EF concurrency token — verified by mutation,
> not assumed. The second seeds rows sharing a `MessageId` across `ConsumerType` values and asserts
> **neither claim exceeds `batchSize`**; that assertion is the one the cross-product bug failed,
> and nothing else in the suite would have caught it.

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
2. **Fresh test double per test** — `FakeMessagePublisher` and `InMemoryOutboxStore` never shared.
   Inbox tests use a fresh isolated SQLite connection per test rather than an in-memory double:
   the claim and the dedup gate are both database behaviour, and a hand-written double would
   assert the behaviour it was written to have
3. **`TenantId` always set** in test fixtures — never null or empty string
4. **SQLite isolation** — integration tests: each `Task.Run` must use its own isolated connection
5. **No `Thread.Sleep` in tests** — use `Task.Delay` with `CancellationToken` if timing matters
6. **`ConfigureAwait(false)` in test helpers** — not in test methods themselves
7. **`GenerateDocumentationFile=false`** in all test `.csproj` files
8. **`NoWarn CS1591;CA1707`** in all test `.csproj` files
