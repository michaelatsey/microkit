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

## Test doubles

> ⚠ **`MicroKit.Messaging.Testing` does not exist.** `src/` holds four projects and none of them
> is it (L0 finding #19). `FakeMessagePublisher`, `InMemoryOutboxStore` and `InMemoryInboxStore`
> are planned types, not available ones. Do not write a test against them, and do not treat a
> reference to them elsewhere in the docs as current.

Until that package is built, the suite uses two mechanisms and nothing else:

```csharp
// ✅ Seams → NSubstitute. Every processor, coordinator and worker takes interfaces.
var store = Substitute.For<IOutboxProcessorStore>();
store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
     .Returns(new OutboxClaim(token, [message]));

// ✅ Clock and jitter → injected, so the back-off curve is asserted against EXACT values.
//    FixedRandom.NoJitter (draw 1.0) yields the ceiling; FixedRandom.ZeroDelay (0.0) yields zero.
new OutboxProcessor(store, scopeFactory, options,
                    new FakeTimeProvider(Now), FixedRandom.NoJitter, NullLogger<OutboxProcessor>.Instance);

// ✅ Store behaviour → the REAL EF store on a fresh isolated SQLite connection, NOT a double.
//    The claim carries no provider-specific SQL, so the production path is what runs.
var store = new EfOutboxStore<TestMessagingDbContext>(ctx, new FakeTimeProvider(Now));

// ✅ Inbox ingestion returns a result — a redelivery is reported, never thrown (ADR-MSG-017)
var result = await inboxWriter.AddAsync(inboxMessage, ct);
result.ShouldBe(InboxWriteResult.Added);

var redelivered = await inboxWriter.AddAsync(Clone(inboxMessage), ct);
redelivered.ShouldBe(InboxWriteResult.AlreadyPresent);   // the nominal path, not an error
```

> **Why the real store rather than a hand-written double.** The claim and the dedup gate are both
> *database* behaviour — an atomic `UPDATE … WHERE` and a unique index. A double asserts the
> behaviour it was written to have, which is exactly the bug class these tests exist to catch.

---

## Test Categories

### Unit Tests (`MicroKit.Messaging.UnitTests`)
- `IMessagePublisher` ingestion (subscriber fan-out, no subscriber, redelivery, cancellation)
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

### Performance Tests — **not implemented**
`tests/MicroKit.Messaging.PerformanceTests/` is an empty directory and is absent from
`MicroKit.Messaging.slnx`. Do not put it in a run command until it holds a project. When it is
written, the paths worth measuring are `ClaimBatchAsync` (the hottest query in the module),
`ApplyOutcomesAsync` at batch sizes above the default 100, and `EfInboxStore.AddAsync` on the
redelivery path, where the savepoint and the verification query are pure overhead.

---

## Mandatory Test Cases Per Component

### OutboxProcessor (claim / settlement)
```
ProcessBatch_WhenClaimEmpty_DoesNotDispatchOrSettle
ProcessBatch_WhenAllSucceed_PublishesEveryMessageAndSettlesOnce
ProcessBatch_SettlesWithTheClaimsOwnToken
ProcessBatch_SettlesOnATokenIndependentOfTheCallersShutdown       (a cancelled settle strands leases)
ProcessBatch_WhenDispatchThrows_BelowThreshold_SchedulesExactRetryInstant
ProcessBatch_WhenDispatchThrows_AtThreshold_DeadLettersInsteadOfRetrying
ProcessBatch_WhenPayloadPermanentlyUndeliverable_DeadLettersWithoutConsumingRetries
ProcessBatch_WhenOneMessageIsPoison_TheRestOfTheBatchStillRuns
ProcessBatch_WhenTransportUnavailable_ReleasesRemainderWithoutConsumingRetries
ProcessBatch_WhenTransportUnavailable_StillSettlesEveryClaimedMessage
ProcessBatch_WhenCancelledMidBatch_ReleasesRemainderWithoutConsumingRetries
ProcessBatch_WhenDispatcherUnregistered_SettlesBatchThenRethrows
ProcessBatch_WhenDispatcherUnregistered_ConsumesNoRetryBudget
ProcessBatch_WhenSettlementThrows_DoesNotTakeTheWorkerDown
ProcessBatch_WhenErrorMessageExceedsLimit_TruncatesIt
ComputeBackoffCeiling_BelowCap_IsTwoToThePowerOfRetryCountSeconds  (exact values, FixedRandom)
ComputeBackoffCeiling_AboveCap_IsClampedToMaxRetryBackoff
ComputeBackoffCeiling_ClampsACorruptedRetryCount
ApplyJitter_DrawsOverTheWholeInterval
```

### Integration Tests (outbox claim — requires a real DB)
```
ClaimBatchAsync_TwoProcessors_ShareNoMessageAndLoseNone
ClaimBatchAsync_UnderRealConcurrency_NeverOverlapsAndNeverLoses
ClaimBatchAsync_WhenManyProcessorsRaceOnOneRow_ExactlyOneWins
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

### Outbox stores (EfOutboxStore, SQLite)

> The per-message lease API these used to cover — `GetPendingAsync`, `AcquireLeaseAsync`,
> `MarkPublishedAsync`, `MarkFailedAsync`, `DeadLetterAsync` — was removed by the outbox claim
> rewrite. It has no replacement to test; the claim and the settlement are the surface now.

```
AddAsync_StagesMessage_NotPersistedUntilSaveChanges
AddBatchAsync_StagesEveryMessage_InOneSaveChanges
ClaimBatchAsync_ReturnsOnlyEligibleMessages
ClaimBatchAsync_ExcludesDeadLettered
ClaimBatchAsync_ExcludesLiveLease
ClaimBatchAsync_IncludesExpiredLease_RecoveringACrashedProcessor    (crash recovery)
ClaimBatchAsync_ExcludesMessageWithFutureNextRetryAtUtc
ClaimBatchAsync_RespectsBatchSize_AndClaimsOldestFirst
ClaimBatchAsync_StampsStatusLeaseAndTokenOnEveryClaimedRow
ClaimBatchAsync_WhenTwoProcessorsRaceOnOneRow_ExactlyOneWins
ApplyOutcomesAsync_Published_SetsTerminalStateAndClearsTheLease
ApplyOutcomesAsync_Retry_PersistsRetryCountAndNextAttempt
ApplyOutcomesAsync_DeadLetter_SetsFailedAndDeadLettered
ApplyOutcomesAsync_Released_ReturnsToPendingWithoutConsumingRetries  (the retry-budget guard)
ApplyOutcomesAsync_WithStaleToken_WritesNothing                      (the lost update)
ApplyOutcomesAsync_WithStaleToken_RejectsFailureWritesToo
ApplyOutcomesAsync_ReportsPartialSettlementViaRowCount
RequeueAsync_WhenNotDeadLettered_ReturnsFalse
GetDeadLetteredAsync_WithNullTenant_ReturnsEveryTenantIncludingNullTenantRows
DeleteProcessedAsync_WithNullTenant_ReachesNullTenantRows
DefaultErrorMessageLength_FitsTheMappedColumn                        (cross-package coupling)
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

### In-process fan-out (InProcessIntegrationDispatcher)

> `InProcessMessagePublisher` and `IMessagePublisher` no longer exist (ADR-MSG-018). The fan-out
> moved into `InProcessIntegrationDispatcher`, which sources every field from the `OutboxMessage`
> row rather than from the event. These run against the real dispatcher with a substituted
> `IInboxWriter` and a real `MessageHandlerRegistry`.

```
DispatchAsync_TakesTheDedupKeyFromTheOutboxRow_NotTheEvent    (the latent bug this closed)
DispatchAsync_TakesEveryFieldFromTheOutboxRow
DispatchAsync_WhenMultipleSubscribersRegistered_WritesOneRowPerConsumer
DispatchAsync_WhenNoSubscriberRegistered_WritesNothingAndDoesNotThrow   (valid, not an error)
DispatchAsync_WhenOneConsumerIsAlreadyPresent_StillWritesTheOthers      (the partial-loss fix)
DispatchAsync_WhenDeserializeReturnsNull_ThrowsOutboxPayloadException
DispatchAsync_WhenTheWriteFailsForReal_Propagates
DispatchAsync_ResolvesConsumersByRuntimeType_NotTheStaticType
```

### Integration event publishing (ADR-MSG-018)

Two properties matter more than the rest and cannot be replaced by reading code:

```
PublishAsync_WithNoOpenTransaction_ThrowsAndStagesNothing   (unit — asserts the row never existed,
                                                             not merely that it threw: a guard
                                                             placed after staging also throws)
PublishAsync_StagesTheRow_WithoutWritingIt                  (integration — asserts the ChangeTracker
                                                             entry is still Added. A rollback test
                                                             passes even if the writer committed
                                                             internally; this one cannot)
```

> Use a **recording fake** for `IIntegrationEventWriter`, never a mock. A negative assertion against
> a mock only holds if it names a method the subject actually calls — `DidNotReceive().AddAsync(...)`
> against code calling something else is green whatever happens, and no mutation exposes it.

```
PublishAsync_WhenEventNotRegistered_ThrowsAndNamesTheType
PublishAsync_StagesTheContractTheSourceAndTheExecutionContext
PublishAsync_WhenCorrelationIdIsUnparseable_DegradesToNullRatherThanFailing
PublishAsync_RecordsOccurrenceTimeSeparatelyFromStagingTime
PublishAsync_ResolvesTheContractFromTheRuntimeType_NotTheGenericArgument
Registry_KeepsEachModulesOwnSource
Registry_WhenTwoModulesClaimOneContractName_IsRejected
Registry_ContractSurface_MatchesTheSnapshot                 (a contract name is public API)
RegistryValidator_OnStart_FailsOnADuplicatedContractName    (drive the hosted service directly —
                                                             starting a host would start four
                                                             messaging workers needing stores the
                                                             test has no reason to wire)
TheWriterAndTheCallersUnitOfWorkShareOneDbContext           (the guard reads the WRITER's context;
                                                             if they diverge it passes while the row
                                                             commits elsewhere)
ThePublisherReadsTheMessageScopesExecutionContext           (pins the L0 #21 fix)
TheClaimTokenIsMappedAsAConcurrencyToken                    (no relay exists to exercise it yet,
                                                             so the model is asserted instead)
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
        typeof(EfOutboxStore<>).Assembly,          // EntityFrameworkCore (generic in TContext)
        // MicroKit.Messaging.Testing goes here once it exists — it does not today.
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

✅ PublishAsync_WhenNoSubscriberRegistered_LogsWarningAndReturns
✅ ExistsAsync_WhenPresent_ReturnsTrue
✅ ProcessBatch_WhenMaxRetriesExceeded_DeadLettersMessage
❌ TestOutboxProcessor
❌ ShouldPublishMessage
```

---

## Rules

1. **No `MediatR.Contracts`** in any test project `.csproj` — zero tolerance. (The glue's own test
   project, `MicroKit.Messaging.MediatR.UnitTests`, transitively references MediatR via the glue
   under test — that is the ADR-MSG-009 carve-out, not a violation.)
2. **Fresh substitute and fresh database per test** — never share an NSubstitute mock or a
   `DbContext` across tests. Store tests use a fresh isolated SQLite connection per test rather
   than an in-memory double: the claim and the dedup gate are both database behaviour, and a
   hand-written double would assert the behaviour it was written to have
3. **`TenantId` set explicitly** in test fixtures — never left to a default. A `null` tenant is
   *valid* in production (single-tenant, ADR-MSG-008 §5) and is the only value that matches rows
   there, so any query filtering on tenant must be tested with `null` as well as a real tenant
4. **SQLite isolation** — integration tests: each `Task.Run` must use its own isolated connection
5. **No `Thread.Sleep` in tests** — use `Task.Delay` with `CancellationToken` if timing matters
6. **`ConfigureAwait(false)` in test helpers** — not in test methods themselves
7. **`GenerateDocumentationFile=false`** in all test `.csproj` files
8. **`NoWarn CS1591;CA1707`** in all test `.csproj` files
