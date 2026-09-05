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
- `MediatROutboxDispatcher` routing by `MessageKind`, and the notification-only composition
  (`IMessagePublisher` ingestion is **gone** — the seam was deleted by ADR-MSG-018 and the
  in-process fan-out behind it by ADR-MSG-019; do not write a test against either)
- `InboxIngestionValidator` — a registered handler with no producer fails at boot
- `OutboxDispatcherKeys.Standard` value snapshot, and the `IMessageSerializer` default's owner
- `OutboxProcessor` state transitions (Pending → Processing → Published/Failed)
- `InboxProcessor` claim, settlement and failure classification (drives `FakeTimeProvider`)
- `OutboxMessage` retry back-off formula verification
- `InboxMessage` compound dedup key isolation (unique index; the PK is the `RowId` surrogate)
- `MessageEnvelope` wire shape (property names and bare-string identifiers — a rename is a
  breaking change for every deployed consumer) and verbatim payload carriage
- `TransportOutboxDispatcher` routing by `MessageKind`, and the no-transport case failing as a
  configuration fault rather than a transient one
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
ProcessBatch_WhenTheScopeCannotSupplyTheOriginHolder_SettlesBatchThenRethrows   ← a foreign container
ProcessBatch_WhenTheScopeCannotSupplyTheOriginHolder_ConsumesNoRetryBudget
ProcessBatch_NamesTheDispatchedRowAsTheCauseOfWorkInItsScope     ← seed a DIFFERENT ancestor cause,
ProcessBatch_WhenTheDispatchedRowIsARoot_StillNamesItAsTheCause     or copy-through passes too
ProcessBatch_PropagatesCorrelationUnchangedWhileDerivingCausation
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
ProcessBatch_NamesTheDeliveredMessageAsTheCauseOfTheHandlersWork  (MessageId, never RowId — seed all
                                                                   three candidates differently)
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

### Outbox dispatch routing (MediatROutboxDispatcher)

> ⚠ **The in-process fan-out is gone.** `IMessagePublisher` and `InProcessMessagePublisher` went
> with ADR-MSG-018; `InProcessIntegrationDispatcher` went with ADR-MSG-019, and its test file with
> it. Nothing writes an `InboxMessage` in this release — do not write a test against a producer that
> does not exist. Re-introduction of the type is blocked by
> `Core_DoesNotContainTypeNamedInProcessIntegrationDispatcher`, and the absence of a producer is
> pinned by `Core_DoesNotDependOnIInboxWriter`.

Routing is now decided by `OutboxMessage.MessageKind`, never by the payload's CLR type. The two
tests that carry the design are marked; the rest would each pass under the old implementation.

```
DispatchAsync_WhenKindIsNotification_PublishesViaMediatR
DispatchAsync_WhenKindIsContract_DelegatesToTheInner
DispatchAsync_WhenKindIsContract_NeverTouchesTheSerializer        ← recording fake, empty call log
DispatchAsync_WhenKindIsContract_AndPayloadIsANotification_StillDelegates   ← kills a CLR-type router
DispatchAsync_WhenKindIsNotification_AndPayloadIsNotANotification_ThrowsOutboxPayloadException
DispatchAsync_WhenKindIsNotification_AndPayloadDoesNotDeserialize_ThrowsOutboxPayloadException
DispatchAsync_WhenKindIsUnknown_DelegatesToTheInner
DispatchAsync_WhenContractAndNoInner_ThrowsOutboxConfigurationException
DispatchAsync_WhenUnknownKindAndNoInner_IsReleasedRatherThanDeadLettered
DispatchAsync_WhenNotificationAndNoInner_StillPublishes
```

> **Why a recording fake for the serializer and not `DidNotReceive()`.** The assertion is that a
> contract row is never deserialized — deserializing in front of `TransportOutboxDispatcher` would
> reinstate the producer-type-graph coupling that class exists to avoid. A mock's
> `DidNotReceive().Deserialize(...)` stays green the moment the subject calls `Serialize` instead.
> An empty call log cannot.

### Composition and registration order (AddMediatRDomainEvents)

> These are the tests for the failure mode that has already shipped once: a second
> `IOutboxDispatcher` descriptor, DI resolving the last one, the decorator bypassed with no
> exception and no log. Descriptor assertions prove *shape*; only the last one proves the *chain*.

```
Registration_WithTransportFirst_ResolvesTheDecorator
Registration_WithGlueFirst_ResolvesTheDecorator
Registration_WithGlueFirst_StillRegistersTheKeyedStandardDispatcher
Registration_InEitherOrder_LeavesExactlyOneUnkeyedDispatcher                (Theory, both orders)
Registration_InEitherOrder_TheDecoratorActuallyReachesTheTransport          ← recording transport
AddMediatRDomainEvents_CalledTwice_ContributesTheSinkOnce
AddMediatRDomainEvents_CalledTwice_LeavesOneDispatcher
Registration_WithNoTransportDispatcher_ComposesAndDispatchesNotifications   (notification-only host)
Registration_WithNoTransportDispatcher_ContractRowIsAConfigurationFault
```

> A fixture that RESOLVES the decorator while a transport dispatcher is registered must also
> register an `IMessageTransport`: the keyed inner is activated when the decorator is constructed,
> so a missing transport fails the whole resolution, notifications included. That is the deliberate
> consequence recorded in ADR-MSG-019, not a fixture defect — the notification-only tests register
> no transport dispatcher at all rather than working around it.

### The unfed inbox (InboxIngestionValidator)

```
StartAsync_WhenNoHandlersRegistered_DoesNotThrow
StartAsync_WhenAHandlerIsRegistered_ThrowsInboxConfigurationException
StartAsync_NamesTheRegisteredConsumersAndTheReason
```

> Driven **directly**, never by starting a host — a host would start four messaging workers needing
> stores these tests have no reason to wire. That is also why every other suite may still call
> `AddMessageHandler` freely: `BuildServiceProvider()` starts no hosted service.

### Integration event publishing (ADR-MSG-018, completed by step 5)

A contract is a `MessageKind.Contract` **outbox row**. `IntegrationEventMessage` and its table are
deleted — do not write a test against either; their return is blocked by
`NoAssemblyStillCarriesTheDedicatedIntegrationEventTable`.

Three properties matter more than the rest and none can be replaced by reading code:

```
PublishAsync_WithNoOpenTransaction_ThrowsAndStagesNothing   (unit — asserts the row never reached
                                                             the writer, not merely that it threw.
                                                             The writer FLUSHES, so a guard placed
                                                             after it would have nothing left to
                                                             prevent: the row is already committed)
PublishAsync_WritesTheRowInsideTheTransaction_AndARollbackErasesIt
                                                            (integration — BOTH halves. An entry left
                                                             Unchanged proves the write happened;
                                                             the rollback proves it was not committed.
                                                             Either alone passes while the other is
                                                             broken)
AbsorbedDuplicate_LeavesTheCallersOwnWritesIntact           (PostgreSQL, ×2 — see below)
```

> Use a **recording fake** for `IIntegrationEventWriter`, never a mock. A negative assertion against
> a mock only holds if it names a method the subject actually calls — `DidNotReceive().AddAsync(...)`
> against code calling something else is green whatever happens, and no mutation exposes it.

```
PublishAsync_WhenEventNotRegistered_ThrowsAndNamesTheType
PublishAsync_StagesAContractRow_WithKindContractNameSourceAndOrigin
PublishAsync_OutsideADispatch_StagesANullOrigin              (null origin ⇒ no dedup, deliberately)
PublishAsync_WhenTheWriterReportsAlreadyPublished_ReturnsTheExistingId
PublishAsync_WhenCorrelationIdIsUnparseable_SubstitutesAFreshOne  (NOT null — the column is required)
PublishAsync_WhenOccurrenceTimeNotSupplied_FallsBackToTheStagingTime
PublishAsync_RecordsOccurrenceTimeSeparatelyFromStagingTime
PublishAsync_ResolvesTheContractFromTheRuntimeType_NotTheGenericArgument
PublishAsync_CapturesTheTraceParent
ProcessBatch_StampsTheDispatchedRowOnTheScopesOriginHolder   (via CONSTRUCTOR injection — the path
                                                              that failed silently as L0 #21)
TheWriterAndTheCallersUnitOfWorkShareOneDbContext            (the guard reads the WRITER's context;
                                                              if they diverge it passes while the row
                                                              commits elsewhere)
ThePublisherReadsTheMessageScopesExecutionContext
Registry_KeepsEachModulesOwnSource
Registry_WhenTwoModulesClaimOneContractName_IsRejected
Registry_ContractSurface_MatchesTheSnapshot                  (a contract name is public API)
RegistryValidator_OnStart_FailsOnADuplicatedContractName
```

### The replay key — PostgreSQL only (`[DockerRequiredFact]`, `PostgreSqlSuite`)

SQLite proves the constraint is declared. It does not reproduce what this suite exists for: on
PostgreSQL a violation aborts the **whole transaction**, so a bare `catch (DbUpdateException)`
passes on SQLite and destroys the caller's transaction here.

```
Replay_IsAbsorbedAndTheTransactionRemainsUsable              (the third write is the assertion)
AbsorbedDuplicate_LeavesTheCallersOwnWritesIntact_WithAutoSavepoints
AbsorbedDuplicate_LeavesTheCallersOwnWritesIntact_WithoutAutoSavepoints
Publish_WithNoOrigin_DoesNotDeduplicate
A_non_duplicate_write_failure_still_throws                   (NOT NULL, never absorbed)
DeleteProcessedAsync_TranslatesTheOriginGuard_AndHonoursIt   (SQLite translating it proves nothing
                                                              about the provider anyone deploys)
```

> **Keep BOTH auto-savepoint cases.** They cover different things and only running both
> distinguishes them: with EF's automatic savepoints ON the writer's explicit savepoint is belt and
> braces and the test passes without it, so a reader seeing only that case concludes it is dead code.
> With them OFF — an ordinary consumer setting — it is load-bearing, and deleting
> `RollbackToSavepointAsync` fails that case with PostgreSQL `25P02`. Verified by mutation.

> These drive `EfIntegrationEventWriter` itself, never a copy of its body. A test that reimplements
> the mechanism it verifies is green for the wrong reason — it keeps passing after the shipped
> writer stops taking a savepoint at all.

### The reentrant hop, end to end (`MicroKit.Messaging.MediatR.IntegrationTests`)

```
ANotificationHandlersPublication_BecomesAContractRow_AndReachesTheTransport
Redelivery_ProducesNoDuplicateContract    (the test ADR-MSG-019 recorded as owed by this step)
```

> The first of the two also carries the **causal chain**: the notification row is asserted a root
> (`CausationId` null — a command-scope publication has nothing above it) and the contract asserted
> to name it, while `CorrelationId` is asserted **unchanged** across the hop. Both halves are
> required. Causation advances one hop per dispatch and correlation never does; a test asserting
> only one of them passes while the other is confused for it — which is precisely how
> `CausationId` stayed null on every row of every path without a red test.

> `Redelivery_ProducesNoDuplicateContract` asserts the handler **did** re-run. Without that, a
> passing test proves only that nothing happened. It also scopes its "no `Warning`" assertion to
> MicroKit's own log categories: EF Core logs the rejected `INSERT` at `Error` on every absorbed
> replay, from a category a library cannot silence because the setting lives on the consumer's
> `DbContext`.

### Claim ordering

```
ClaimBatchAsync_RespectsBatchSize_AndClaimsOldestByCreatedAtUtc   (CreatedAtUtc and OccurredOnUtc
                                                                   seeded in OPPOSITE orders, or the
                                                                   test passes under both impls)
GetDeadLetteredAsync_StillOrdersOnOccurredOnUtc                   (the deliberate asymmetry)
TheDispatchableIndex_SortsOnCreatedAtUtc                          (a mismatch costs no correctness
                                                                   and no test — only every poll)
```

### Retention — the replay key's real bound

```
DeleteProcessedAsync_KeepsAContractWhoseOriginCanStillBeDispatched
DeleteProcessedAsync_PurgesAContractWhoseOriginIsTerminal
DeleteProcessedAsync_PurgesAContractWithNoOrigin
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
        typeof(MessagingBuilder).Assembly,         // Core
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
