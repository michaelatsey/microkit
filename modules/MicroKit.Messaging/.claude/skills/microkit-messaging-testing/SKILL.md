# Skill: microkit-messaging-testing

How to run, filter, and write tests for MicroKit.Messaging.

> ⚠ Rewritten after the outbox (#87) and inbox (#90) claim rewrites. The per-message lease API
> (`GetPendingAsync`, `AcquireLeaseAsync`, `MarkPublishedAsync`, `MarkFailedAsync`,
> `DeadLetterAsync`, `MarkProcessingAsync`, `MarkProcessedAsync`) no longer exists, and
> **`MicroKit.Messaging.Testing` has not been built** — see "Test doubles" below for what the
> suite does instead.

---

## Projects that exist

| Project | What it covers |
|---|---|
| `MicroKit.Messaging.UnitTests` | processors, workers, coordinators, publisher, serializer |
| `MicroKit.Messaging.IntegrationTests` | EF stores on SQLite; `PostgreSql/` subset behind Docker |
| `MicroKit.Messaging.ArchitectureTests` | dependency boundaries, ADR-MSG-009 carve-out |
| `MicroKit.Messaging.MediatR.UnitTests` | glue registration, sink, dispatcher, message factory |
| `MicroKit.Messaging.MediatR.IntegrationTests` | end-to-end domain event → outbox → notification |

There is **no** `MicroKit.Messaging.PerformanceTests`. The directory exists but is empty and is
absent from `MicroKit.Messaging.slnx`; do not add it to a run command until it holds a project.

---

## Run

```bash
# Everything (this is the gate — must be green before merge)
dotnet test modules/MicroKit.Messaging/MicroKit.Messaging.slnx -c Release

# One project
dotnet test modules/MicroKit.Messaging/tests/MicroKit.Messaging.UnitTests/
dotnet test modules/MicroKit.Messaging/tests/MicroKit.Messaging.IntegrationTests/
dotnet test modules/MicroKit.Messaging/tests/MicroKit.Messaging.ArchitectureTests/
```

### Filter

```bash
# The claim path, both sides
--filter "FullyQualifiedName~ClaimBatchAsync"

# Settlement, including the token guard
--filter "FullyQualifiedName~ApplyOutcomes"

# Ingestion dedup (NOT ~ExistsAsync — ExistsAsync is a diagnostic, not the gate)
--filter "FullyQualifiedName~Redelivery|FullyQualifiedName~duplicate"

# Retry / dead-letter / back-off
--filter "FullyQualifiedName~Backoff|FullyQualifiedName~DeadLetter|FullyQualifiedName~Retry"
```

### PostgreSQL subset

`tests/.../PostgreSql/` uses `[DockerRequiredFact]` and skips silently without Docker. Two of
those tests decide whether the design is correct and cannot be replaced by reading code:

- `InboxLeaseExpiryTests.WhenTheLeaseExpiresMidHandler_TheLoserRollsBackEntirelyAndTheWinnerStands`
  — fails if `ClaimToken` is not mapped as an EF concurrency token. Verified by mutation.
- `InboxClaimConcurrencyTests.ClaimBatchAsync_TwoProcessorsOverAFannedOutQueue_AreDisjointAndBounded`
  — seeds rows sharing a `MessageId` across `ConsumerType` values and asserts **neither claim
  exceeds `batchSize`**. That is the assertion the compound-key cross-product bug failed.

**Run them before merging anything that touches a claim, a settlement or an EF configuration.**

---

## Test doubles — what the suite actually uses

`MicroKit.Messaging.Testing` is planned but **not implemented**; `src/` holds four projects and
none of them is it (L0 finding #19). Until it exists, follow what the suite does:

```csharp
// ✅ Seams → NSubstitute. Processors, coordinators and workers take interfaces; substitute them.
var store = Substitute.For<IOutboxProcessorStore>();
store.ClaimBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
     .Returns(new OutboxClaim(token, [message]));

// ✅ Clock and jitter → injected, so back-off is asserted against EXACT values, not a tolerance.
var processor = new OutboxProcessor(
    store, scopeFactory, options,
    new FakeTimeProvider(Now),          // Microsoft.Extensions.TimeProvider.Testing
    FixedRandom.NoJitter,               // draw = 1.0 → the ceiling exactly; TestFixtures.cs
                                        // (FixedRandom.ZeroDelay is the 0.0 counterpart)
    NullLogger<OutboxProcessor>.Instance);

// ✅ Store behaviour → the REAL EF store on an isolated SQLite connection. The claim carries no
//    provider-specific SQL, so the production code path is exactly what runs.
var conn = new SqliteConnection("Data Source=:memory:");
conn.Open();
var ctx = new TestMessagingDbContext(
    new DbContextOptionsBuilder<TestMessagingDbContext>().UseSqlite(conn).Options);
ctx.Database.EnsureCreated();
var store = new EfOutboxStore<TestMessagingDbContext>(ctx, new FakeTimeProvider(Now));
```

> **Why the real store and not a hand-written in-memory double.** The claim and the dedup gate are
> both *database* behaviour — an atomic `UPDATE … WHERE` and a unique index. A double would assert
> the behaviour it was written to have, which is precisely the bug class these tests exist to
> catch.

---

## Writing tests — the constraints that bite

```csharp
// ✅ One isolated connection per test, and per Task.Run inside a test.
private static (SqliteConnection conn, TestMessagingDbContext ctx) CreateIsolatedDb() { ... }

// ✅ Concurrency tests need a SECOND context over the SAME connection — one processor's
//    change tracker must not be able to see the other's staged work.
private static TestMessagingDbContext SecondContext(SqliteConnection conn) => new(...);

// ❌ Shared static database name — state bleeds between tests.
private static readonly string _dbName = "shared-test-db";
```

- **Shouldly only.** FluentAssertions is banned (Xceed commercial licence v8+). No `.Should().`
- **`TenantId`** — set it in fixtures. `null` is *valid* in production (single-tenant,
  ADR-MSG-008 §5), so test both a real tenant and `null` where a query filters on it.
- **No `Thread.Sleep`.** Drive `FakeTimeProvider` instead; that is why it is injected.
- **`GenerateDocumentationFile=false`** and **`NoWarn CS1591;CA1707`** in every test `.csproj`.

---

## Banned-library checks

```bash
# FluentAssertions — must return nothing
grep -rn "FluentAssertions\|\.Should()\." modules/MicroKit.Messaging/ --include="*.cs"

# MediatR — must return hits ONLY under MicroKit.Messaging.MediatR{,.UnitTests,.IntegrationTests}
grep -rln "MediatR" modules/MicroKit.Messaging/src modules/MicroKit.Messaging/tests --include="*.cs"
```

> The second command **is expected to produce output**. `MicroKit.Messaging.MediatR` is the single
> package permitted to reference MediatR / MediatR.Contracts (ADR-MSG-009 carve-out), and its two
> test projects reference it transitively. A hit anywhere else is a violation.
> `AllAssemblies_HaveNoMediatRContractsDependency` enforces exactly this split — trust the
> architecture test over a grep.

---

## Architecture tests

```bash
dotnet test modules/MicroKit.Messaging/tests/MicroKit.Messaging.ArchitectureTests/ -v normal
```

Any failure is a layer-boundary violation. Two are worth knowing about before you touch them:

- `Core_DoesNotContainTypeNamedMessageDispatcher` — pins the removal of the old `MessageDispatcher`
  in favour of the `IOutboxDispatcher` seam. Re-introducing that name is blocked deliberately.
- `Core_DependsOn{SharedDbOutbox,SharedDbInbox}Coordinator_OnlyThroughI*Coordinator` — the
  concrete coordinators stay `internal sealed` so a future per-tenant coordinator composes the
  public engine rather than reimplementing it.
