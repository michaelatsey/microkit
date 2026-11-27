# microkit-messaging-testing

How the suite is built and which tests carry the design. Running and filtering:
`.claude/skills/microkit-messaging-testing/SKILL.md`.

---

## Libraries

| Library | Use |
|---|---|
| xUnit (v2: the `xunit` package) | Runner. `[Fact]`, `[Theory]`; `[DockerRequiredFact]` derives from `FactAttribute` |
| Shouldly | Assertions |
| NSubstitute | Doubles for seams with no negative assertion to make |
| NetArchTest.Rules | Dependency tests (`MicroKit.Messaging.ArchitectureTests`) |
| Testcontainers.PostgreSql | The PostgreSQL suites |
| Microsoft.Extensions.TimeProvider.Testing | `FakeTimeProvider` |
| Microsoft.EntityFrameworkCore.Sqlite | The fast store suite |
| **FluentAssertions** | **Banned** (commercial licence). No `.Should()` |
| **MediatR / MediatR.Contracts** | Only `MicroKit.Messaging.MediatR` and its two test projects reach them, through `MicroKit.MediatR`; no `.csproj` references either package directly (ADR-MSG-009). `MicroKit.Messaging.ArchitectureTests` receives them transitively through the glue it inspects |

There is no `MicroKit.Messaging.Testing` package. `tests/MicroKit.Messaging.PerformanceTests/` is
an empty directory, absent from `MicroKit.Messaging.slnx`.

---

## Test doubles — what the suite uses

| For | Use | Why |
|---|---|---|
| Store behaviour (claim, settlement, guard, unique index) | The real `EfOutboxStore<TContext>` / `EfInboxStore<TContext>` over `TestMessagingDbContext` on an isolated SQLite in-memory connection; PostgreSQL when concurrency or uniqueness is the point | They are database behaviour — an atomic `UPDATE … WHERE`, a unique index, a concurrency token. A double asserts what it was written to do |
| The pipeline's guard in unit tests | `InMemoryInboxGuard` over `InMemoryInboxDatabase` (`GuardHarness`) | Scripts the faults a real store raises: `FailNextCommit`, `FailAfterNextCommit`, `FailInsertFailed`, `FailReads`, `RetryOn` |
| A seam whose calls matter in order, or fail per message | Hand-written recording or scripted fakes: `ScriptedDispatcher`, `RecordingMessageTransport`, `RecordingInboxHandler`, `RecordingLocalSender`, `RecordingSerializer`, `CapturingLogger<T>` / `CapturingLoggerProvider` | See below |
| A seam with a canned answer | `Substitute.For<IOutboxProcessorStore>()` and the like | |
| Clock and jitter | `FakeTimeProvider`; `FixedRandom.NoJitter` (draw 1.0 → the ceiling) and `FixedRandom.ZeroDelay` (0.0 → zero) | Back-off is asserted against exact values, not a range |
| A host | `BusTestHost` (bus over the in-memory transport), `GuardHost` (PostgreSQL), `E2EHarness` (MediatR end to end) | |

> **A recording fake, never `DidNotReceive()`, for a negative assertion.** `DidNotReceive().SendAsync(...)`
> holds only if it names the member the subject would call; against code calling something else it
> is green whatever happens, and no mutation exposes it. An empty `Sent` list cannot be satisfied
> that way (`RecordingMessageTransport`).

Internal types are reachable from `MicroKit.Messaging.UnitTests`, `MicroKit.Messaging.IntegrationTests`
and `MicroKit.Messaging.MediatR.IntegrationTests` (`InternalsVisibleTo`); `OutboxProcessorTests`
builds the processor directly with a `ServiceCollection` holding a keyed `IOutboxDispatcher`.

---

## Rules

1. **Fresh state per test.** Never share a substitute, a `DbContext` or a database across tests.
   Each SQLite test opens its own `Data Source=:memory:` connection (`CreateIsolatedDb()`); a second
   processor is a **second context on the same connection** (`SecondContext(conn)`), so one change
   tracker cannot see the other's work.
2. **Uniqueness and concurrency run on PostgreSQL.** SQLite serializes writers and does not
   reproduce a blocked `UPDATE` re-evaluating its `WHERE`, two transactions racing on a unique index,
   or a concurrency-token conflict. `[DockerRequiredFact]`, `[Collection(PostgreSqlSuite.Name)]`.
3. **Set `TenantId` explicitly.** `null` is valid in production and the only value that matches rows
   in a single-tenant deployment: a query filtering on tenant is tested with `null` and a real one.
4. **Two consumers whenever fan-out is the subject.** One row per handler, a skipped handler not
   costing the next its delivery, a correlation minted once and shared — all invisible with one.
5. **Seed the values a bug would confuse differently.** Two clocks in opposite orders; a different
   ancestor causation than the message id; copied and derived causation asserted separately.
6. **No `Thread.Sleep`.** Drive `FakeTimeProvider`, or wait on a condition (`Eventually`).
7. **No `ConfigureAwait(false)` in test methods.**
8. **Every test `.csproj`:** `GenerateDocumentationFile=false`, `NoWarn` including `CS1591;CA1707`,
   `IsPackable=false`, unconditional `ProjectReference`s.
9. **A test names a member that exists.** A name citing a removed type is a test nobody can maintain.

---

## Mutation checks

A test that guards a line is shown red with that line broken, before it is trusted:

- Break the guarded path (delete the token filter, the concurrency-token mapping, the `Contains`
  on `RowId`…), run the test, see it fail, restore.
- Back the file up first and restore with plain `cp` from the backup — never `git checkout`, which
  also wipes the branch's uncommitted work. Not `cp -p`: a restored file keeping its old mtime makes
  the next run execute the mutated binary. Prove the rebuild (grep a marker, prefer a full build).
- Change only the property under test: keep exception text and event ids verbatim, or an unrelated
  assertion goes red.
- A mutation that turns nothing red is a finding: coverage is missing.

`MicroKit.Messaging.UnitTests/Readme/ReadmeSnippetTests` is the same idea for documentation: every
`<!-- snippet: name -->` block of the README must equal a compiled region of a `ReadmeSnippets.cs`.

---

## Tests that carry the design, by component

Grep the test projects for the full list. These are the ones whose removal or weakening loses a
guarantee.

### The bus (`UnitTests/Bus`)
```
MessageBusTests.PublishAsync_BuildsTheEnvelopeFromTheContractTheClockAndTheSerializer
MessageBusTests.PublishAsync_WithNoTransport_ThrowsAConfigurationFault
MessageBusTests.PublishAsync_WhenTheExplicitTenantContradictsTheAmbientOne_ThrowsAndSendsNothing
MessageBusTests.PublishAsync_WhenASendMiddlewareDoesNotProceed_ThrowsRatherThanSucceedingSilently
MessageBusTests.PublishAsync_ReservedHeadersSetByASendMiddleware_AreOverwrittenByTheAmbientContext
BusEndToEndTests.PublishAsync_WithATenant_PropagatesTenantCorrelationAndCausationThroughAHandlersPublication
BusEndToEndTests.HostShutdown_MidHandler_RedeliversToTheNextHostWithoutCountingAFailedAttempt
BusEndToEndTests.ScopeInitializer_RunsBeforeTheHandlersConstructorDependenciesAreCreated
BusEndToEndTests.AReceiveMiddlewareThatProceedsTwice_RunsTheRestOfTheChainTwice   ← a retrying middleware
InMemoryTransportTests.ADependencyUnavailable_PausesTheEndpoint_AndRedeliversWithoutCountingAnAttempt
InMemoryTransportTests.AddMicroKitMessaging_RegistersNoHostedServiceButItsValidator
MalformedEnvelopeTests.ASerializerThrowingOperationCanceledDuringShutdown_RequeuesWithTheAttemptCountUnchanged
SafetyNetTests.WithAThrowingLogger_AFaultInTheDeliveryPath_DeadLettersOnce_AndTheLoopKeepsConsuming
```

### The receive pipeline and the guard (`UnitTests/Bus`, `UnitTests/Processing`)
```
MessageReceivePipelineTests.DeliverAsync_WithTwoHandlers_RunsBothAndReturnsTheMostRedeliverableVerdict
MessageReceivePipelineTests.DeliverAsync_WhenTheHostIsStopping_IsUnsettledAndStopsBeforeTheNextHandler
MessageReceivePipelineTests.DeliverAsync_WhenTheEnvelopeHasNoCorrelation_MintsOneSharedByEveryHandler
EnvelopeReceiverTests.ReceiveAsync_WhenContractNameIsUnbound_DeadLettersAndRunsNothing
EnvelopeReceiverTests.ReceiveAsync_WhenOneHandlersRowExists_SkipsItAndStillRunsTheOthers     ← two handlers
EnvelopeReceiverTests.ReceiveAsync_WhenTheRowExistsInAnyState_NeverStartsASecondAttempt
EnvelopeReceiverTests.ReceiveAsync_WhenAHandlerFails_WritesItsRowFailed_RunsTheOthers_AndAcknowledges
EnvelopeReceiverTests.ReceiveAsync_WhenTheFailedRowCannotBeWritten_AsksForARedelivery
EnvelopeReceiverTests.ReceiveAsync_WhenTheStoreReportsItselfUnreachable_ForTheFailedRow_AsksToPauseAndRedeliver
EnvelopeReceiverTests.ReceiveAsync_WhenAHandlerThrowsPermanently_DeadLettersItsRow_AndAcknowledges
EnvelopeReceiverTests.ReceiveAsync_WhenADependencyIsDown_WritesNoRow_AndAsksToPauseAndRedeliver
EnvelopeReceiverTests.ReceiveAsync_WhenTheHostIsStopping_LeavesTheMessageUnsettled
EnvelopeReceiverTests.ReceiveAsync_StampsEventTypeFromTheResolvedLocalType_NotFromTheEnvelope ← through the real serializer
EnvelopeReceiverTests.ReceiveAsync_StampsReceivedAtUtcFromTheClock_NotFromOccurredOnUtc
EnvelopeReceiverTests.ReceiveAsync_BuildsEachScopeWithTheTenantCorrelationAndDerivedCausation
EnvelopeReceiverTests.ReceiveAsync_WhenTheGuardCannotBeActivated_AsksForARedelivery_RatherThanAConfigurationVerdict
EnvelopeReceiverTests.ReceiveAsync_RunsTheScopeInitializersBeforeTheStoreIsBuilt_InEveryScope
EnvelopeReceiverTests.ReceiveAsync_WhenItsOwnCommitLandedAndThenFaulted_IsNotCountedAsAConcurrentDuplicate
EnvelopeReceiverTests.ReceiveAsync_WhenNoHandlerIsRegistered_CountsIt                         ← a MeterListener
```

### The guard on PostgreSQL (`IntegrationTests/PostgreSql`)
```
HandlerIsolationTests.TwoHandlers_OneFailing_TheOtherRunsOnce_AndTheFailingOneIsRetriedToProcessed
HandlerIsolationTests.ARedeliveryWhileTheRowIsFailed_IsSkipped_AndTheRetryRunsOnce
HandlerIsolationTests.AHandlerThatSavesPublishesThenThrows_CommitsNothing_AndItsRowIsFailed
HandlerIsolationTests.AHandlerThatPublishesThenThrows_ReportsItsPublicationDiscardedWithTheAttempt_NotLost  (4001, never 4000)
HandlerIsolationTests.AHandlerThatSavesThenPublishes_CommitsItsDataItsPublicationAndItsRowTogether
HandlerIsolationTests.ARetryWhoseClaimIsLostMidHandler_RecordsNoFailure_AndLeavesTheAttemptCount
HandlerIsolationTests.AFailedRowThatCannotBeWritten_LeavesTheDeliveryUnacknowledged_AndItIsRedelivered
HandlerIsolationTests.ARowTheStoreRefusesOnItsData_IsACountedFailure_NotADependencyOutage   ← a tenant id past its column
HandlerIsolationTests.ARetriedHandlerHittingABusinessUniqueKey_CountsEachAttempt_AndDeadLettersAfterTheMax
ExactlyOnceTests.TwoConcurrentDeliveries_BothPastTheRead_GiveOneEffect         ← the unique index under a real race
ExactlyOnceTests.AHostLostBetweenTheCommitAndTheDispatch_IsDrainedByAnother_AndEachEffectAppliesOnce
ExactlyOnceTests.WithARetryingStrategy_AFaultBeforeTheCommit_IsReplayed_AndEveryEffectAppliesOnce
ExactlyOnceTests.WithARetryingStrategy_AFaultAfterTheCommit_IsVerified_AndNothingIsReplayed
ExactlyOnceTests.WithARetryingStrategy_ATransientFaultOfTheVerificationRead_IsRetried_AndTheCommitStands   ← the strategy sees the raw fault
ExactlyOnceTests.ARetryAbandonedWithItsClaim_IsRecoveredAfterTheLease_AndTheEffectAppliesOnce
ExactlyOnceTests.ARetryAbandonedWithItsClaim_IsRecoveredAfterTheLease_WhenTheWallClockStepsBack   ← B's clock 5 s behind
ExactlyOnceTests.ARetryAbandonedWithItsClaim_IsRecoveredAfterTheLease_WhenTheClockStepsBackBeforeTheClaim
```
- A lease is wall-clock time, stamped from the system clock and compared with it. Wait for the
  condition (the stamped lease past by the claimer's clock), never for a fixed multiple of the lease,
  and keep deadlines on `Stopwatch`: WSL2 stepped this machine's clock back 402–619 ms every ~32 s,
  which once failed the fixed-wait version of `…AndTheEffectAppliesOnce` under full-suite load;
  `…WhenTheWallClockStepsBack` replays that step before B waits, `…BeforeTheClaim` between B's check
  and its claim. B runs on a recording clock: an empty claim is retried only when the `now` that claim
  read was not past A's lease, and anything else fails; the offset variant first proves the claim
  reads B's clock, the step variant that the claim right after its step was empty. Their failure message carries the row's timeline, wall and
  monotonic side by side.

### Outbox enlistment and stores (`IntegrationTests/Stores`, `UnitTests/Stores`, `IntegrationTests/PostgreSql`)
```
OutboxEnlistmentTests.PublishAsync_EnlistsTheRowWithoutSaving_AndTheCallersSaveCommitsIt
OutboxEnlistmentTests.PublishAsync_WhenTheCallersTransactionRollsBack_LeavesNeitherTheRowNorTheBusinessData
OutboxEnlistmentTests.AnEnlistedRowItsScopeNeverSaved_IsReportedAsLost                       (event 4000)
OutboxEnlistmentTests.AnEnlistedRowDiscardedWithTheTracker_IsReportedAtDebugOnly             (event 4001)
OutboxEnlistmentTests.AnEnlistedRowUnsavedInAHostsOwnTransaction_IsStillReportedAsLost      (UseTransaction: 4000, not 4001)
OutboxEnlistmentTests.AScopeDisposedSynchronously_DoesNotThrow_AndStillReportsALostRow       (IDisposable)
LocalGuardOutageTests.ALocalRowWhoseConsumersStoreIsDown_IsDeferred_NotDeadLettered_AndTheStoresOtherRowsKeepTheirCadence   (2209 carries the store's reason)
GuardReadCancellationTests.AHostStopDuringTheGuardsRead_IsUnsettled_NeverAnOutage          (D10)
ConsumerKeyMigrationTests.TheChangelogStatements_RewriteEveryKey_IntoTheKeyTheCodeBuilds_EvenForAMessageSplitAcrossVersions  (PostgreSQL; reads the SQL from CHANGELOG.md)
StoreResolutionTests.*                       — every rule of the first-match order, and its refusals
StoreCompositionTests.AnAssemblyBoundToTwoStores_IsAProblem
TwoStoresTests.InsideADelivery_OnAReplayedAttempt_APublicationStillLandsInTheDeliverysStore
TwoStoresTests.WithTwoStores_AnUnboundHandler_FailsAtStart
StoreIsolationTests.WithOneStoresDatabaseStopped_TheOtherKeepsItsCadence_AndTheStoppedOneBacksOffAndRecovers
EfStoreStartupCheckTests.AStoreWhoseModelLacksTheMessagingEntities_FailsAtStart
EfCoreOutboxRegistrationTests.UseEntityFrameworkOutbox_RegistersTheEngineOnce_AndTheStoreServicesUnderItsKey
```

### OutboxProcessor (`UnitTests/Processing`)
```
ProcessBatch_WhenDispatchThrows_BelowThreshold_SchedulesExactRetryInstant       (FixedRandom, exact)
ProcessBatch_WhenDispatchThrows_AtThreshold_DeadLettersInsteadOfRetrying
ProcessBatch_WhenPayloadPermanentlyUndeliverable_DeadLettersWithoutConsumingRetries
ProcessBatch_WhenOneMessageIsPoison_TheRestOfTheBatchStillRuns
ProcessBatch_WhenTransportUnavailable_DefersItsEndpointsRemainderWithoutConsumingRetries
ProcessBatch_WhenCancelledMidBatch_ReleasesRemainderWithoutConsumingRetries
ProcessBatch_WhenDispatcherUnregistered_SettlesBatchThenRethrows
ProcessBatch_WhenTheDispatcherCannotBeActivated_DefersItsEndpointsRowsWithoutConsumingRetriesOrRethrowing
ProcessBatch_WhenTheDispatcherItselfThrowsInvalidOperationException_RetriesOnlyThatMessage
ProcessBatch_WhenActivationThrowsAnythingButInvalidOperationException_RetriesOnlyThatMessage
ProcessBatch_WhenDispatcherActivationIsCancelled_ReleasesAsCancelledRatherThanAsAnActivationFault
ProcessBatch_WhenTheProviderIsDisposedDuringActivation_ReleasesTheRowWithoutChargingARetry
ProcessBatch_WhenTheProviderIsDisposedWhileTheHostRuns_RetriesOnlyThatRow
ProcessBatch_SettlesUnderTheClaimToken
ProcessBatch_WhenSettlementThrows_DoesNotPropagate
ProcessBatch_NamesItsStoreOnEveryDispatchScope
ProcessBatch_NamesTheDispatchedRowAsTheCauseOfWorkInItsScope   ← seed a different ancestor cause
ProcessBatch_PropagatesCorrelationUnchangedWhileDerivingCausation
ComputeBackoffCeiling_BelowCap_IsTwoToThePowerOfRetryCountSeconds / _AboveCap_ / _WithCorruptRetryCount_DoesNotOverflow
OutboxTransportClassificationTests.ProcessBatch_WhenATransportRowHasNoDispatcher_ReleasesBatchThenRethrows
OutboxTransportClassificationTests.ProcessBatch_WhenARowsEndpointCannotBeRouted_DeadLettersItAndDispatchesTheRest
EndpointIsolationTests.AMixedBatchWithNoTransport_DeliversItsLocalRows_AndDefersOnlyItsTransportRows
EndpointIsolationTests.ADownEndpoint_IsTriedOncePerBackOffPeriod_NotOncePerCycle_AndItsBackOffGrows
IntegrationTests EndpointOutageClaimTests.ABrokerOutageLongerThanABatch_NeverStarvesTheLocalRows_AndTriesTheBrokerOncePerBackOff
```
The activation tests fail activation for **one** row — the shape a tenant-scoped cause has: that
tenant is deferred and the other tenants' rows dispatched (#124); a row with no tenant keeps the
endpoint verdict (`…ForARowWithNoTenant_DefersItsEndpointsRows`); the `InvalidOperationException`
boundary is pinned on both sides. #188's endpoint tests (`EndpointIsolationTests`) use rows with no
tenant. The tenant back-off is pinned by `TenantBackoffTests` (unit) and, on PostgreSQL,
`TenantBackoffOutboxTests` (V1–V3, V1b, a known tenant recovering while the endpoint is suspected,
V11–V12: a one-tenant store's claims leave it out and write nothing until its back-off ends),
`TenantBackoffInboxTests` (retries, new deliveries, the in-memory transport not paused, V11–V12),
`TenantClaimFilterTests` (each endpoint's list, the inbox's pairs and consumers; never a row with no
tenant, under `UseRelationalNulls()` too), `TenantExclusionPlanTests` (the outbox: one array parameter
per list, one SQL text, nothing for an empty list; the inbox: two array parameters for any exclusion,
either possibly empty, one SQL text, main's text for none; no write, the published history not walked, no more read than
the deferred backlog cost before A7; the inbox's pair claim reading no more than main's tenant claim,
D11 of #210, its medians in the test's comment; with the local endpoint also excluded, the local backlog's heap not walked — a
planner property no code mutation turns red, so keep the local backlog larger than the held tenant's),
`AdminDeadLetterPendingTests` and `ShutdownReleaseTests.AHostStoppedDuringTheSecondPassOf…`. The inbox's
back-off per (consumer, tenant) (#210) is pinned by `InboxBackoffTests`, `InboxBackoffEquivalenceTests`
(with one consumer it answers as `TenantBackoff` on generated sequences, the seed in the message) and
`InboxBackoffQuietTests` (unit), and on PostgreSQL by `ConsumerBackoffInboxTests` (two consumers of one
message, and three for V16's second case: V1, V3–V5, V7, V8, V16 — with a held backlog filling a whole
batch too — V17) and `ConsumerTenantClaimTests` (the pair key matched exactly, an empty tenant id a tenant).

### InboxProcessor and the inbox retry policy (`UnitTests/Processing`)
```
ProcessBatch_WhenTheRetrySucceeds_MarksTheRowProcessedInItsTransaction_AndWritesNoOutcome
ProcessBatch_WhenTheClaimIsLostBeforeTheHandler_RunsNothingAndWritesNothing
ProcessBatch_WhenTheClaimIsLostMidHandler_RecordsNoFailureAndCountsNoAttempt
ProcessBatch_WhenARetriedHandlerHitsABusinessUniqueViolation_CountsTheAttempt   ← not a lost lease
ProcessBatch_WhenConsumerNotRegistered_DeadLettersOnFirstSight
ProcessBatch_WhenTheHandlerThrowsPermanentMessageException_DeadLettersOnFirstSight
ProcessBatch_HandsTheHandlerTheContextOfTheFirstDelivery_RebuiltFromTheRow
ProcessBatch_NamesTheDeliveredMessageAsTheCauseOfTheHandlersWork               (MessageId, never RowId)
ProcessBatch_WhenTheCommitFailsAfterReachingTheDatabase_TheVerificationEndsIt
ProcessBatch_WhenDependencyUnavailable_ReleasesTheRemainderWithoutRetries
ProcessBatch_WhenTheStoreRegistersNoGuard_ReleasesTheBatchThenRethrows
ProcessBatch_WhenTheGuardCannotBeActivated_RetriesTheRowRatherThanStopping
ProcessBatch_WhenTheHostStopsMidHandler_ReleasesTheRowWithItsAttemptCountUnchanged
ProcessBatch_SettlesOnATokenIndependentOfTheCallersShutdown                    (a cancelled settle strands leases)
InboxRetryPolicyTests.ComputeBackoffCeiling_FollowsTheExponentialCurve
```

### The loops (`UnitTests/Processing`, `IntegrationTests/PostgreSql`)
```
OutboxLoopTests / InboxLoopTests: NextDelay_* (saturated, idle, partial, outage, cancelled, from zero) — the outbox has no outage arm: a down endpoint backs off on its own rows
OutboxLoopTests.RunAsync_WhenTheProcessorCannotBeActivated_LogsAnActivationFailureAndKeepsRunning
OutboxLoopTests / InboxLoopTests.RunAsync_WhenTheProcessorThrowsAConfigurationException_BacksOffAndKeepsRunning   (1402 never ends a loop)
InboxRetentionLoopTests.DefaultRetentionWindow_IsLongerThanTheOutboxs
OutboxRetentionLoopTests.ExecuteAsync_DeletesAcrossEveryTenant
GuardedLoopTests.AFault_WhileTheHostRuns_LeavesTheLoopRunning   ← every loop × {disposed while running, throwing logger}
GuardedLoopTests.AFaultOfAnInMemoryWorkersLoop_IsReported_NeverSwallowed      (2305, 3017)
GuardedLoopTests.AnUnsettledDeliveryWhileTheHostRuns_IsAnOutage_OnlyWhenTheRootContainerIsDisposed
GuardedLoopTests.TheLocalQueue_ / TheInMemoryTransport_AHandlersOwnDisposedProvider_WithTheContainerHealthy_IsCounted_AndDeadLetters
ShutdownReleaseTests.AHostStoppedMidOutboxBatch_ReleasesEveryClaimedRowWithItsAttemptCountUnchanged
ShutdownReleaseTests.AHostStoppedMidInboxBatch_ReleasesEveryClaimedRowWithItsAttemptCountUnchanged
```

### Partitioned stores (#191: `UnitTests/Processing`, `IntegrationTests/PostgreSql/Instances`)
```
FifoGateTests.Release_HandsThePermitToTheOldestWaiter_InArrivalOrder / AReleasedPermit_GoesToTheWaiter_NotToANewcomer
FifoGateTests.AReleaseRacingACancellation_NeverLosesAPermit                       (10,000 rounds)
StoreInstanceReconcilerTests.ManySignals_BeforeAndWhileItRuns_ThrowNothing_AndTheLastSnapshotIsWhatIsDrained
StoreInstanceReconcilerTests.ARemovedInstance_StopsAndIsReleased_AndReAddedStartsAgainOnlyOnceItsLoopsHaveEnded
InstanceLoopTests.ARemovedInstance_SettlesItsBatchInFlight_ThenStopsClaiming / ABatchThatThrows_ReturnsItsPermit_AndTheLoopGoesOn
InstanceMetricsTests.*                                                             (V7: tag off sums, tag on, released)
PartitionedStoreTests.FiftyInstances_EveryRowAndDueRetry_IsProcessedInItsOwnDatabase  (V1, peak connections)
PartitionedStoreTests.AnInstanceAddedAtRuntime_…  / RemovedWhileABatchIsInFlight_…  / ADeliveryWhoseDependencyIsDown…
PartitionedStoreTests.ADeliveryInFlightAcrossARemoval_… / ACheckOnlyAnInstanceCanRun_… / AnUnmappedDelivery_DoesNotDelayAMappedOne_…
PartitionedStoreLatencyTests.WithOneInstancesDatabaseStopped_… (V4, 600 ms) / UnderAGateOfOne_TwoLargeBacklogs_… (V5, 3.7 s)
PartitionedHostShapeTests.TheReadmesPartitionedStore_StartsAndDrains_UnderDevelopmentValidation (V10)
```
- The instances' databases live on their own container (`InstanceServerFixture`), started with
  `max_connections=600`; each database's connection string caps its pool at 10 and prunes idle
  connections after 3 s, so 50 databases stay within the server. Each is cloned from a template carrying
  the schema. A database is "stopped" with `ALTER DATABASE … ALLOW_CONNECTIONS false` and its sessions
  terminated, so one server serves every test.
- V4 and V5's bounds are measured (five runs each), the derivation next to the constant. V5 gives each
  heavy send a 1 ms cost: an unfair gate then costs the light row tens of seconds, not a margin.

### Dispatchers (`UnitTests/Dispatch`, `UnitTests/Local`, `UnitTests/DI`)
```
TransportOutboxDispatcherTests.DispatchAsync_DoesNotDeserializeThePayload
TransportOutboxDispatcherTests.DispatchAsync_UsesTheStagedSource_NotOneResolvedAtDispatch
TransportOutboxDispatcherTests.DispatchAsync_CarriesTheRowsStagedTraceParent_NotTheRelaysTrace
TransportOutboxDispatcherTests.DispatchAsync_PropagatesTransportUnavailable / _PropagatesPayloadRejection / _PropagatesAnUnclassifiedFailure
LocalOutboxDispatcherTests.ARowWhoseTypeResolvesNowhere_IsPermanent
MessagingBuilderTransportTests.TheRowsEndpoint_NamesItsDispatcher
MessagingBuilderTransportTests.AStore_RegistersBothDispatchers_WhateverTheTransport
MessagingBuilderTransportTests.AStoreWithAProvidersOwnTransport_SendsItsTransportRowsThroughIt
```
`DoesNotDeserializeThePayload` is structural first — the dispatcher's constructor has no
serializer, so adding one breaks the file at compile time — and behavioural second: a body that is
not JSON reaches the transport unchanged.

### EF stores on SQLite (`IntegrationTests/Stores`)
```
EfOutboxStoreTests.ClaimBatchAsync_IncludesExpiredLease_RecoveringACrashedProcessor
EfOutboxStoreTests.ClaimBatchAsync_RespectsBatchSize_AndClaimsOldestByCreatedAtUtc   ← clocks seeded opposite
EfOutboxStoreTests.GetDeadLetteredAsync_StillOrdersOnOccurredOnUtc                   (the deliberate asymmetry)
EfOutboxStoreTests.ApplyOutcomesAsync_Released_ReturnsToPendingWithoutConsumingRetries
EfOutboxStoreTests.ApplyOutcomesAsync_WithStaleToken_WritesNothing / _RejectsFailureWritesToo
EfOutboxStoreTests.ApplyOutcomesAsync_ReportsPartialSettlementViaRowCount
EfOutboxStoreTests.DeleteProcessedAsync_WithNullTenant_ReachesNullTenantRows
EfOutboxStoreTests.DefaultErrorMessageLength_FitsTheMappedColumn                     (cross-package coupling)
EfOutboxStoreTests.TheOutboxTable_HasNoUniqueIndexBesideItsKey
EfOutboxStoreTests.TheDispatchableIndex_SortsOnCreatedAtUtc
EfInboxStoreTests.EnlistProcessedAsync_WhenAnotherDeliveryCommittedThePairFirst_FailsTheCommit
EfInboxStoreTests.BeginAsync_WhenTheContextAlreadyHasATransaction_Refuses
EfInboxStoreTests.TryInsertFailedAsync_WhenTheFailureIsNotTheGuard_Throws
EfInboxStoreTests.ClaimBatchAsync_NeverExceedsBatchSize                               (the cross-product bound)
EfInboxStoreTests.ClaimBatchAsync_OrdersOnReceivedAtUtc_NotOnOccurredOnUtc            ← clocks seeded opposite
EfInboxStoreTests.ClaimBatchAsync_ClaimsAFailedRowOnceItsRetryIsDue_AndNeverADeadLetteredOne
EfInboxStoreTests.ApplyOutcomesAsync_DoesNotTouchASiblingConsumersRow
EfInboxStoreTests.MarkClaimedProcessedAsync_WithAStaleToken_ReturnsFalse
EfInboxStoreTests.IsLeaseLost_DiscriminatesStructurally
EfInboxStoreTests.NoIndexTouchesOccurredOnUtc_AndTheClaimIndexSortsOnReceivedAtUtc
InboxSettlementGuaranteeTests.TheGuardAndTheHandlerScopeShareOneUnitOfWork
InboxSettlementGuaranteeTests.CommitAsync_WritesTheRow_WhenTheHandlerClearedTheTracker
InboxSettlementGuaranteeTests.ProcessBatch_SettlesTheRow_WhenTheContextDefaultsToNoTracking
```

### Claims and settlement on PostgreSQL (`IntegrationTests/PostgreSql`)
```
InboxLeaseExpiryTests.WhenTheLeaseExpiresMidHandler_TheLoserRollsBackEntirelyAndTheWinnerStands
InboxClaimConcurrencyTests.ClaimBatchAsync_TwoProcessorsOverAFannedOutQueue_AreDisjointAndBounded
InboxClaimConcurrencyTests.ClaimBatchAsync_WhenManyProcessorsRaceOnOneRow_ExactlyOneWins
OutboxClaimConcurrencyTests.ClaimBatchAsync_UnderRealConcurrency_NeverOverlapsAndNeverLoses
OutboxClaimConcurrencyTests.ApplyOutcomesAsync_WithStaleToken_IsRejectedByTheTokenGuard
SettlementStrategyTests.UnderEnableRetryOnFailure_AnOutboxBatchIsSettled_AndNothingIsResentAfterTheLease
SettlementStrategyTests.{Outbox,Inbox}Settlement_WhenItsCommitLandsAndThenFaults_IsVerified_AndReportsTheCountItWrote
```
- `WhenTheLeaseExpiresMidHandler…` is the test that decides whether the ownership mechanism is
  real: without `ClaimToken` mapped as a concurrency token the loser overwrites the winner and every
  other test still passes.
- `…OverAFannedOutQueue_AreDisjointAndBounded` seeds rows sharing a `MessageId` across
  `ConsumerType` values and asserts no claim exceeds `batchSize` — the assertion the compound-key
  cross-product claim failed.

### The local endpoint and the MediatR glue
```
LocalEndpointWithoutStoreTests.TwoConsumers_OneFailingTransiently_TheOtherRunsExactlyOnce_AndTheFailingOneIsRetriedUntilItSucceeds
LocalEndpointWithoutStoreTests.ALocalMessage_IsDeliveredToEachConsumer_EachInAScopeOfItsOwn_NeverTheSenders
LocalEndpointWithoutStoreTests.WithTwoStores_ALocalConsumerBoundToNeither_FailsAtStart_NamingItAndTheFix
MessagingMediatRRegistrationTests.RegistersNoDomainEventsDispatcher_NoNotificationPublisher_AndNoSerializer
MessagingMediatRRegistrationTests.AtStart_AHandlerThatCannotBeAConsumer_FailsNamingIt_EvenWhenRegisteredAfterTheGlue
NotificationHandlerSourceTests.AConsumer_IsNotFailedByASiblingThatCannotBeBuilt_AndDisposesItsOwnHandler
NotificationHandlerSourceTests.AFactoryRegisteredHandler_IsReported_NamingItsNotification
NotificationHandlerSourceTests.ASingletonHandler_IsReported_NamingTheFix
MediatR.IntegrationTests NoStoreDomainEventTests.WithoutAStore_EventsPendingWhenAHandlersScopeEnds_AreNotDispatched_AndAreReported  (2310)
DomainEventNotificationSinkTests.ReceiveAsync_SendsOnlyTheMappedEvents_InDrainOrder_EachUnderItsEventsIdentityAndTime
MediatR.IntegrationTests PostgreSql/LocalEndpointTests.TwoHandlers_OneFailing_TheOtherRunsOnce_AndTheFailingOneIsRetriedFromItsRow
MediatR.IntegrationTests PostgreSql/LocalEndpointTests.ACascadeRaisedByABusHandler_IsDeliveredOnce_AndARedeliveryRunsNothing
MediatR.IntegrationTests PostgreSql/TwoStoreDomainEventTests.ANotificationBoundToStoreB_RaisedInAUnitOfWorkThatSavesStoreA_IsReportedUnsaved
MediatR.IntegrationTests CascadeDeliveryTests.ACascadeEvent_RaisedByANotificationHandler_CommitsWithItsWork_AndIsDeliveredOnce
MediatR.IntegrationTests ReceivingSeamTests.AnEnvelopeCrossingTheSeam_CausesTheHandlersWorkThroughTheInboxRow
MediatR.IntegrationTests ReceivingSeamTests.Redelivery_ThroughTheSeam_CostsNoConsumerItsRow
MediatR.IntegrationTests ReentrantOutboxTests.ANotificationHandlersPublication_BecomesAContractRow_AndReachesTheTransport
```
- `AnEnvelopeCrossingTheSeam…` asserts copied and derived causation **separately**: the row's
  `CausationId` is the notification that caused the contract (copied inbound); the handler's
  context names the message itself (derived). The two values differ on purpose.
- The two halves of a seam test share nothing but a `MessageEnvelope` taken from
  `RecordingMessageTransport.Sent` and handed to `IEnvelopeReceiver`. Anything more is a fan-out
  wearing a wire's clothes.
- A "nothing at `Warning`" assertion is scoped to MicroKit's own log categories: EF Core logs a
  rejected `INSERT` from its own category, which a library cannot silence.

### Composition, contracts and the wire (`UnitTests/DI`, `/Publishing`, `/Transport`, `/Serialization`)
```
SingleEntryPointTests.EveryCompositionProblem_IsListedInOneExceptionAtHostStart
SingleEntryPointTests.AnInvalidComposition_BindsNoEndpoint
SingleEntryPointTests.APublicationWithNoTransport_FailsAtHostStart
SingleEntryPointTests.AStoreMissingItsServices_FailsAtHostStart
MessagingSerializerDefaultTests.AddMicroKitMessaging_IsWhatRegistersTheDefault_NotABuilderMethod
IntegrationEventRegistryTests.Registry_ContractSurface_MatchesTheSnapshot          (a contract name is public API)
MessageConsumerKeyTests.TheKey_IsIdenticalForTwoAssemblyVersionsOfTheSameType      (the inbox key survives a version bump)
IntegrationEventRegistryTests.Registry_WhenTwoModulesClaimOneWireType_IsRejected
IntegrationEventRegistryTests.ResolvedLocalType_RoundTripsThroughTheSerializer
MessageContractAttributeTests.Constructor_WhenTheNameAlreadyEndsInAVersionSuffix_Throws
MessageEnvelopeTests.MessageEnvelope_HasNoPositionalConstructor_SoAddingAHeaderNeverChangesOne
MessageEnvelopeTests.MessageHeaders_AreTheFourLowerCaseCloudEventsExtensions
OutboxMessageFactoryTests.CreateContract_WithANonJsonSerializer_RefusesRatherThanCorruptingTheRow
EndpointBindingTests.APublicationBeforeHostStart_ReachesTheHostsOwnHandlerOnceItStarts
```

---

## Architecture tests (`MicroKit.Messaging.ArchitectureTests`)

- `StoreContractTests.IOutboxProcessorStore_HasOneClaim_TakingTheEndpointAndTenantFilters_WithNoDefaultImplementation`,
  `…IInboxProcessorStore_HasOneClaim_TakingTheConsumerAndPairExclusions_WithNoDefaultImplementation` — a
  default that ignored a filter would let a store provider bring back local-row starvation, or the
  re-deferral of a held tenant's, pair's or consumer's backlog (#124, #210).
- `PublicSurfaceTests` — the full exported surface of all four assemblies, both directions (an
  unexpected type fails, a listed type that no longer exists fails). Uses
  `Assembly.GetExportedTypes()`, not NetArchTest, which breaks under coverlet instrumentation.
- `MessagingAbstractionsArchitectureTests` — no EF Core / ASP.NET Core / MediatR in Abstractions;
  no EF Core / MediatR / broker client in Core; no MediatR / ASP.NET Core in EntityFrameworkCore; no
  EF Core / ASP.NET Core / broker client in the glue; `AllAssemblies_HaveNoMediatRContractsDependency`
  over Abstractions, Core and EntityFrameworkCore;
  `OnlyCoreShipsAnIMessageTransport_AndItIsTheInternalInMemoryOne`;
  `NoOutboxDispatcherWritesInboxRows` (no dispatcher depends on `IInboxGuardStore`; the glue does not
  name `IOutboxDispatcher`); `Core_DependsOnTheStaticStoreSet_OnlyThroughIMessageStoreSet`;
  `Core_DoesNotContainTypeNamedMessageDispatcher`,
  `Core_DoesNotContainTypeNamedInProcessIntegrationDispatcher`,
  `NoAssemblyStillCarriesTheDedicatedIntegrationEventTable`.
- An emptiness assertion carries a **control**: first assert the query can match something, or it
  is green for the wrong reason.

---

## Naming

`{Member}_{Scenario}_{ExpectedResult}` for one member, `{Situation}_{Outcome}` for a behaviour
across components; PascalCase segments joined by `_` (`CA1707` is suppressed in test projects).
See `microkit-messaging-naming.md`.
