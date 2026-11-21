# Skill: microkit-messaging-testing

How to run, filter, and write tests for MicroKit.Messaging. The rules — doubles, the tests that
carry the design, mutation checks — are in `.claude/rules/microkit-messaging-testing.md`.

---

## Projects

| Project | What it covers |
|---|---|
| `MicroKit.Messaging.UnitTests` | The bus and the in-memory transport, the receive pipeline and its guard (over `InMemoryInboxGuard`), `EnvelopeReceiver`, the processors and loops, the dispatchers, the local endpoint without a store, store resolution, composition and options, contracts, the serializer, the envelope, README snippets |
| `MicroKit.Messaging.IntegrationTests` | `Stores/`: the EF stores and enlistment on SQLite. `PostgreSql/`: concurrency, the guard under real transactions, settlement under a retrying strategy, shutdown release, several stores, store isolation |
| `MicroKit.Messaging.IntegrationTests.SecondModule` | Not a test project: a second assembly, so the multi-store tests can bind two assemblies |
| `MicroKit.Messaging.ArchitectureTests` | `PublicSurfaceTests` (the exported surface, name by name) and the dependency rules |
| `MicroKit.Messaging.MediatR.UnitTests` | The glue's registration and start checks, `NotificationHandlerSource`, `DomainEventNotificationSink` |
| `MicroKit.Messaging.MediatR.IntegrationTests` | Domain events end to end on SQLite (nominal path, cascade, the receiving seam, the reentrant hop); `PostgreSql/`: the local endpoint under the guard, two stores; README snippets of the durable examples |

`tests/MicroKit.Messaging.PerformanceTests/` is an empty directory, absent from
`MicroKit.Messaging.slnx`: do not put it in a run command.

---

## Run

Always `-c Release`: `TreatWarningsAsErrors` applies to Release only.

```bash
# The gate. CI=true makes the PostgreSQL suites run — and fail without Docker — instead of skipping.
CI=true dotnet test modules/MicroKit.Messaging/MicroKit.Messaging.slnx -c Release

# One project
dotnet test modules/MicroKit.Messaging/tests/MicroKit.Messaging.UnitTests/ -c Release
dotnet test modules/MicroKit.Messaging/tests/MicroKit.Messaging.IntegrationTests/ -c Release
dotnet test modules/MicroKit.Messaging/tests/MicroKit.Messaging.ArchitectureTests/ -c Release
```

### Filter

```bash
# The claim, both sides; settlement and the token guard
--filter "FullyQualifiedName~ClaimBatchAsync"
--filter "FullyQualifiedName~ApplyOutcomes"

# The inbox guard: unit, then PostgreSQL
--filter "FullyQualifiedName~EnvelopeReceiverTests|FullyQualifiedName~MessageReceivePipelineTests"
--filter "FullyQualifiedName~HandlerIsolationTests|FullyQualifiedName~ExactlyOnceTests"

# Retry, back-off, dead-letter
--filter "FullyQualifiedName~Backoff|FullyQualifiedName~Jitter|FullyQualifiedName~DeadLetter|FullyQualifiedName~Retry"

# Stores: resolution, binding, several stores
--filter "FullyQualifiedName~StoreResolutionTests|FullyQualifiedName~StoreCompositionTests|FullyQualifiedName~TwoStoresTests"

# Every PostgreSQL test of a project (namespace …IntegrationTests.PostgreSql)
--filter "FullyQualifiedName~.PostgreSql."
```

---

## The PostgreSQL suites

They live in `tests/MicroKit.Messaging.IntegrationTests/PostgreSql/` and
`tests/MicroKit.Messaging.MediatR.IntegrationTests/PostgreSql/`. The second compiles the first's
`PostgreSqlFixture.cs`, `DockerRequiredFactAttribute.cs` and `StoreDatabase.cs` as linked files.
Tests join `[Collection(PostgreSqlSuite.Name)]`, which shares one container per test run;
`StoreIsolationTests` adds a `StoppablePostgreSqlFixture` it can stop mid-test.

**When `[DockerRequiredFact]` skips** (`DockerRequiredFactAttribute.cs`, `DockerEndpoint.ShouldRunFor`):
only when **both** hold —

- no Docker daemon answers Testcontainers' endpoint discovery
  (`TestcontainersSettings.OS.DockerEndpointAuthConfig` is null, probed once per process, bounded at
  10 seconds; a socket file with no daemon behind it counts as no Docker), **and**
- the `CI` environment variable is neither `true` (any case) nor `1`.

Under CI without Docker the tests **run and fail**: a suite skipped in CI is a suite that silently
stopped running. Locally without Docker they skip with a reason naming the fix. `DockerEndpointTests`
pins this decision and needs no Docker.

**Run them before merging anything that touches a claim, a settlement, the guard, the execution
strategy or an EF configuration.** SQLite cannot reproduce a blocked `UPDATE` re-evaluating its
`WHERE`, two transactions racing on the unique index, or a concurrency-token conflict.

---

## Test doubles — what to reach for

```csharp
// ✅ A seam with a canned answer → NSubstitute.
var store = Substitute.For<IOutboxProcessorStore>();
store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<OutboxEndpoints>(), Arg.Any<CancellationToken>())
     .Returns(new OutboxClaim(Guid.NewGuid(), [message]));

// ✅ The processor over a keyed dispatcher, a fixed clock and a fixed jitter draw
//    (OutboxProcessorTests.Build): back-off is asserted against exact values.
services.AddKeyedSingleton<IOutboxDispatcher>(OutboxDispatchers.Transport, new ScriptedDispatcher());
new OutboxProcessor(store, new TestExecutionScopeFactory(scopeFactory), options,
    new FakeTimeProvider(Now), FixedRandom.NoJitter, NullLogger<OutboxProcessor>.Instance, TestStore);

// ✅ Store behaviour → the real EF store on an isolated SQLite connection (EfOutboxStoreTests).
var conn = new SqliteConnection("Data Source=:memory:");
conn.Open();
var ctx = new TestMessagingDbContext(
    new DbContextOptionsBuilder<TestMessagingDbContext>().UseSqlite(conn).Options);
ctx.Database.EnsureCreated();
var store = new EfOutboxStore<TestMessagingDbContext>(ctx, new FakeTimeProvider(Now));

// ✅ A second processor → a second context on the SAME connection.
await using var ctxB = new TestMessagingDbContext(
    new DbContextOptionsBuilder<TestMessagingDbContext>().UseSqlite(conn).Options);

// ✅ The pipeline's guard in a unit test → GuardHarness over InMemoryInboxDatabase, whose
//    FailNextCommit / FailAfterNextCommit / FailInsertFailed / FailReads / RetryOn script the faults.

// ✅ "Nothing was sent" → RecordingMessageTransport.Sent is empty — never DidNotReceive().
```

`FixedRandom.NoJitter` draws 1.0 (the delay is the ceiling); `FixedRandom.ZeroDelay` draws 0.0.
Both are in `tests/MicroKit.Messaging.UnitTests/TestFixtures.cs`, with `ScriptedDispatcher`,
`RecordingMessageTransport`, `InMemoryInboxGuard` and the row builders `OutboxFixtures` /
`InboxFixtures`.

---

## Writing tests — the constraints that bite

- **Shouldly only.** No FluentAssertions, no `.Should()`.
- **`TenantId`** set in fixtures; test `null` and a real tenant wherever a query filters on it.
- **Two handlers** whenever fan-out, skipping or a shared correlation is the subject.
- **No `Thread.Sleep`.** Drive `FakeTimeProvider`, or wait on a condition (`Eventually` in
  `BusTestHost.cs`).
- **No `ConfigureAwait(false)` in a test method.**
- **Log assertions** go through `CapturingLogger<T>` / `CapturingLoggerProvider`, by event id, and
  are scoped to MicroKit's own categories.
- **Every test `.csproj`:** `IsPackable=false`, `GenerateDocumentationFile=false`, `NoWarn` including
  `CS1591;CA1707`, unconditional `ProjectReference`s, no `Version=`.
- **A README example is code.** A block marked `<!-- snippet: name -->` must equal the region of the
  same name in `tests/MicroKit.Messaging.UnitTests/Readme/ReadmeSnippets.cs` or
  `tests/MicroKit.Messaging.MediatR.IntegrationTests/Readme/ReadmeSnippets.cs`; change both together.
- **A fix comes with a test shown red when the fix is reverted** (mutation check, in the rule).

---

## Banned-library checks

```bash
# FluentAssertions — must return nothing
grep -rn "FluentAssertions\|\.Should()\." modules/MicroKit.Messaging/src modules/MicroKit.Messaging/tests --include="*.cs"

# A direct MediatR package reference — must return nothing: the glue reaches MediatR through
# MicroKit.MediatR, and so do its two test projects
grep -rn 'Include="MediatR' modules/MicroKit.Messaging --include="*.csproj"
```

A text grep for `MediatR` in `.cs` files is not a check: XML docs across the module mention it. The
architecture tests are the authority — `Core_HasNoMediatRDependency`,
`Abstractions_HasNoMediatRDependency`, `EfCore_HasNoMediatRDependency`,
`AllAssemblies_HaveNoMediatRContractsDependency`.

---

## Architecture tests

```bash
dotnet test modules/MicroKit.Messaging/tests/MicroKit.Messaging.ArchitectureTests/ -c Release
```

- **`PublicSurfaceTests`** fails on a public type that is not in its lists **and** on a listed type
  that no longer exists. Adding a public type is an edit there, with the reason in the PR.
- **`NoOutboxDispatcherWritesInboxRows`** — no `IOutboxDispatcher` depends on `IInboxGuardStore`, and
  the glue does not name `IOutboxDispatcher`. Inbox rows are written by the pipeline's guard only.
- **`OnlyCoreShipsAnIMessageTransport_AndItIsTheInternalInMemoryOne`** — a second transport, or a
  public one, is a decision.
- **`Core_DependsOnTheStaticStoreSet_OnlyThroughIMessageStoreSet`** — keeps the store set
  replaceable (#191).
- **`Core_DoesNotContainTypeNamedMessageDispatcher`**,
  **`Core_DoesNotContainTypeNamedInProcessIntegrationDispatcher`**,
  **`NoAssemblyStillCarriesTheDedicatedIntegrationEventTable`** — removed designs stay removed.
