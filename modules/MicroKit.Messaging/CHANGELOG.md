# Changelog — MicroKit.Messaging

## [Unreleased] — outbox and inbox claim rewrites

Both sides of the module move off the per-message lease and onto an atomic batch claim carrying an
ownership token. Round trips per batch drop from `2N+1` to two or three plus one settlement — flat
in `N` — on the outbox, and to three on the inbox.

**The inbox is deliberately not a mirror of the outbox**, and that is the whole of its design. An
outbox may batch its settlement: widening the crash window there widens only duplication, and the
inbox absorbs it downstream. For the inbox there is no downstream — the inbox *is* the absorber, so
a crash between a handler returning and its row being marked reruns the handler with its business
side effects. Inbox **success** therefore settles inside the handler's own transaction; only
failures and releases are batched.

See ADR-MSG-015 (outbox) and ADR-MSG-017 (inbox).

### Changed — the MediatR package decorates instead of replacing, and routes on `MessageKind`

`MediatROutboxDispatcher` no longer deserializes every payload and branches on
`payload is INotification`. It reads `OutboxMessage.MessageKind`: a `Notification` row is published
in process, everything else is delegated inward **without being deserialized at all**.

- **A contract row never touches `IMessageSerializer` on this path.** Deserializing in front of
  `TransportOutboxDispatcher` would make dispatch depend on the producer's type graph — the one
  thing that does not cross a process boundary — reinstating the coupling that class was built to
  avoid while leaving it looking correct. This also removes the double deserialization the previous
  decorator performed on every integration event.
- **The disjointness assumption is gone.** Routing by CLR type required assuming `IIntegrationEvent`
  and `IDomainEventNotification` could never overlap; nothing enforced that. The column decides.
- **A `Notification` row whose payload is not an `INotification`** is now an `OutboxPayloadException`
  (dead-letter on first sight) rather than being delegated inward, where the inner would answer with
  a configuration fault telling the operator to install a package they already have.

### Changed — the outbox dispatcher seam has two slots, and registration order no longer matters

`AddTransportDispatcher()` now makes two registrations: the dispatcher under the keyed slot
`OutboxDispatcherKeys.Standard`, which only `MicroKit.Messaging` writes, plus an unkeyed `TryAdd`
forwarder. `AddMediatRDomainEvents()` removes unkeyed `IOutboxDispatcher` descriptors, takes that
slot, and resolves its inner through the key.

| Order | Before | After |
|---|---|---|
| transport then glue | correct | correct |
| glue then transport | **threw at registration**, and once `TryAdd` landed, silently left the transport dispatcher unregistered | correct |
| glue alone | threw at registration | correct, see below |
| glue twice | guarded by a marker descriptor nothing asserted | one descriptor, structurally |

`OutboxDispatcherKeys.Standard` is public API and a compatibility commitment: changing the string
unlinks a decorator from its inner with no compile error and no startup failure.

**A notification-only host is now a first-class composition.** `AddMediatRDomainEvents()` alone, no
transport method: the keyed lookup yields null, notifications dispatch, and a `Contract` row raises
`OutboxConfigurationException` — released, no retry consumed, drains once a transport is deployed.

Behavioural, both directions. Calling `AddTransportDispatcher()` **without** registering an
`IMessageTransport` now stops notification rows as well as contract rows, because the decorator
activates its keyed inner when constructed. Calling that method declares an intent to send
contracts; a host that only fans out notifications should not call it. See ADR-MSG-019.

### Removed — the in-process fan-out

- **`InProcessIntegrationDispatcher` is deleted.** It wrote inbox rows on the *producing* side,
  which is the confusion the contract-name indirection exists to remove. A `Contract` row now
  travels to a transport as a `MessageEnvelope` and the receiving side writes its own inbox rows.
- **`AddInProcessTransport()` is removed outright** — a breaking change on a preview package. It
  would otherwise have survived as a public method registering nothing but a JSON serializer.
  **Migration:** call `AddTransportDispatcher()`, or nothing at all for a notification-only host.
- The `IMessageSerializer` default moves to **`AddMicroKitMessaging()`**, which is where it belongs:
  that method registers `InboxProcessor` and `OutboxMessageFactory`, and both require one. The
  duplicate `TryAdd`s in `AddIntegrationEventPublishing()` and `AddMediatRDomainEvents()` are
  removed as unreachable — both are extensions on the builder `AddMicroKitMessaging()` returns.

### Changed — the inbox has no producer, and a registered handler now fails at boot

`InProcessIntegrationDispatcher` held the only call to `IInboxWriter.AddAsync` in the module.
**Nothing writes an `InboxMessage` in this release.**

`InboxProcessor`, `InboxWorker`, `SharedDbInboxCoordinator`, both retention workers, all five inbox
store interfaces and the whole claim/settlement mechanism are **unchanged, still correct, and still
registered** — they simply have no producer, which is not the same as being broken. A host that
writes inbox rows itself can drive the entire drain, and the integration suite does exactly that.

**`AddMessageHandler<THandler, TEvent>()` therefore fails at startup.** A new
`InboxIngestionValidator` throws `InboxConfigurationException` naming the registered consumers and
the reason. Without it the gap is undetectable: no row is written, the processor claims nothing,
logs nothing above `Debug`, and reports a healthy empty queue indistinguishable from an idle one.
Like `IntegrationEventRegistryValidator` it is a hosted service, so it reaches a real host only —
containers built with `BuildServiceProvider()` in tests are unaffected. It is removed when the
receiving seam ships.

`Core_DoesNotDependOnIInboxWriter` pins this and is **expected to fail** when that seam arrives.

**Removed test:** `InboxRedeliveryTests` — the fan-out was its entire subject. ADR-MSG-018 cited it
as the evidence blocking this deletion; ADR-MSG-019 records the five properties it proved, what the
transport now covers, and the one end-to-end assertion the receiving seam owes back.

See ADR-MSG-019.

### Added — outbox schema: message kind, contract name, replay natural key

The outbox becomes **reentrant**: one table, two natures of row, distinguished by an explicit column
rather than a CLR type test. A message may pass through the queue twice — once as a `Notification`
fanned out in process, once as a `Contract` handed to a transport.

- **`OutboxMessage.MessageKind`** (`Notification` | `Contract`) — the routing decision, declared on
  the row and queryable in SQL. Stored as a string: the column exists so an operator can ask what is
  in the queue, and an int discriminator is unreadable in `psql` or the Supabase dashboard.
- **`OutboxMessage.ContractName`** — the stable wire identity, e.g.
  `saasbtp.safety.constat-recorded.v1`. `EventType` cannot serve: it holds an assembly-qualified
  name the receiving process cannot resolve, so it works in process by accident and fails across a
  service boundary. `EventType` is now documented for what it is — a local deserialization detail.
- **`OutboxMessage.SourceMessageId`** — the `Id` of the outbox row whose dispatch produced this one.
  Deliberately **not** named after causation: `CorrelationId` and `CausationId` are tracing values
  that degrade to null when unparseable, and this one cannot, because a silent null switches
  deduplication off.
- **`UX_OutboxMessages_Source_ContractName`** — unique over `(SourceMessageId, ContractName)`. A
  redelivered dispatch re-runs its handlers, which publish the same contract from the same source
  row, so the second write collides instead of duplicating. This covers a crash occurring *after*
  the commit, which per-message settlement alone cannot.

Both new nullable columns are meaningful only for a `Contract` row. The pairing is not enforced by
the entity — it is an EF Core entity with no constructor to enforce it in, and a guard in a setter
would throw part-way through materialization. Enforcement belongs to whoever builds the row.

**Two handlers of one notification must not publish the same contract.** They would collide on this
key and the second would be absorbed as a duplicate. Forbidden by convention, not detected.

#### Provider support for the unique index

This index carries a **model invariant**, not a performance hint, so the module declares where it
holds rather than leaving a consumer to reconstruct it:

| Provider | Supported | Why |
|---|---|---|
| PostgreSQL | ✅ | Nulls are distinct in a unique index; `NULLS NOT DISTINCT` is opt-in (PG15+) and is not used |
| SQLite | ✅ | Same rule |
| **SQL Server** | ❌ **not supported** | Nulls compare **equal**, so at most one `(NULL, NULL)` row exists — the second notification row ever written is rejected |

Every notification row carries `(NULL, NULL)`, which is why the distinction decides the whole table.
The SQL Server failure is not visible at DDL time and not on the first row: it appears on the
*second* insert, in production, as a uniqueness violation on a column pair nobody connects to
notifications.

Note the scope on the supported providers too: a `NULL` on either side is distinct, so a contract
staged outside a dispatch does not deduplicate. Intended — the key guards the replay path, where a
source row always exists.

**Migration** (PostgreSQL / SQLite):

```sql
ALTER TABLE "OutboxMessages" ADD COLUMN "MessageKind"     varchar(32)  NOT NULL DEFAULT 'Notification';
ALTER TABLE "OutboxMessages" ADD COLUMN "ContractName"    varchar(256) NULL;
ALTER TABLE "OutboxMessages" ADD COLUMN "SourceMessageId" uuid         NULL;

CREATE UNIQUE INDEX "UX_OutboxMessages_Source_ContractName"
    ON "OutboxMessages" ("SourceMessageId", "ContractName");
```

`'Notification'` is the correct default rather than a convenient one: every row written before this
column existed came through the domain-event path and carries a notification payload. It is also the
zero value of the enum, so a row predating the column and a fixture omitting the property agree.

> Footnote, for anyone who must run this on SQL Server anyway: a filtered index
> (`WHERE "SourceMessageId" IS NOT NULL AND "ContractName" IS NOT NULL`) restores the behaviour. It
> is not shipped — `HasFilter` takes provider-specific SQL and this is the provider-neutral EF Core
> package — and it is a workaround you own, not a supported configuration.

### Added — the contract registry gains the `ContractName` → `Type` direction

`IntegrationEventRegistry` was publish-only: it answered `Type` → contract and nothing else. It now
answers both directions, which is the precondition for a transport to exist at all.

**Why the reverse direction is not optional.** A consumer receives a payload and a name. It does not
hold the producer's assembly, so `Type.GetType(assemblyQualifiedName)` cannot give it anything to
deserialize into — that works in process purely by accident. The map it needs is
`name → its own local type`: the local end of a binding whose other end is a string. Without it a
receiving process can only dead-letter everything it is sent.

- **`IntegrationEventRegistry.TryResolveLocalType(name, out type)` / `ResolveLocalType(name)`** — the
  reverse direction. Matching is **ordinal**: a contract name is a wire identity, not display text,
  and a culture-sensitive comparer silently resolves two distinct contracts to one local type.
  `ResolveLocalType` throws because an unbound name is *permanent* — it cannot become bound without
  a redeploy, so a drain path should dead-letter on first sight rather than spend a retry budget
  re-reaching a verdict it already reached.
- **`IntegrationEventRegistry.SubscribedContractNames`** — snapshot-testable, like `ContractNames`,
  and it matters more: in a service that only consumes, a renamed `[IntegrationEvent]` on a local
  type binds nothing, the old name simply stops arriving, and no publish-side snapshot notices.
- **`IntegrationEventSubscription`, `IntegrationEventSubscriptions`, `IntegrationEventSubscriptionBuilder`**
  — the consuming half of the composition, mirroring the publishing half.
- **`AddIntegrationEventSubscriptions(configure)`** — once per module, declaring the contracts that
  module *understands*. It takes **no `source`**, and that is structural rather than an oversight: a
  source names the module that *emitted* an event and is written onto the staged row as the emitter's
  identity. A consumer emitted nothing, so any value it supplied would be false data in the one
  column that must survive a module's extraction into its own service.
- **`AddIntegrationEventConsumption()`** — once per application. The registry and its startup
  validator and nothing else: a service that only consumes must not be handed a publisher it cannot
  legitimately use. It exists so that service still gets boot-time validation — otherwise its
  registry composes lazily on first resolve, which on a drain path is inside a handler, inside a
  transaction, where a duplicated contract name is misclassified as transient and retried forever
  against something no retry can fix. Safe alongside `AddIntegrationEventPublishing()` in either
  order: one `TryAdd`ed registry, one `TryAddEnumerable`d validator.

**Publishing a contract also binds its name.** `Publishes<T>()` makes that name resolvable to `T`
with no second declaration, so a modular monolith routes its own contracts for free. `Consumes<T>()`
is for a contract a module does not publish itself, and declaring both is a **no-op rather than a
conflict** — a module must not have to know whether its dependency happens to be in-process.

**One local type per contract name, per process.** Two *different* types claiming one name is a boot
failure, whichever side they come from. It has to be: a message is deserialized once, before any
fan-out, so a second claimant could only be honoured by picking arbitrarily — and structurally
compatible JSON yields a plausible wrong object rather than an error. Modules sharing a contract
share the type; per-consumer mirror types are not expressible.

**Not a breaking change.** `IntegrationEventRegistry`'s constructor gained a second parameter with no
compatibility overload — a default of `[]` would build a registry that resolves nothing by name and
dead-letters everything, which is the silent-success class this module refuses. It breaks nobody:
the type is absent from `1.0.0-preview.4` and has never shipped. The same applies to
`Resolve(Type)` → **`ResolveContract(Type)`**, renamed now that the registry answers in two
directions and `Resolve` alone no longer says which one is meant.

### Added — the transport seam: `IMessageTransport`, `MessageEnvelope`, `TransportOutboxDispatcher`

A message can now leave the process. Three pieces land together, and Core alone — with no MediatR
glue installed — finally has a defined, loud behaviour for every row it can meet.

- **`IMessageTransport`** — one method, `SendAsync(MessageEnvelope, CancellationToken)`. The seam a
  broker provider implements.
- **`MessageEnvelope`** — the wire format. See the compatibility note below.
- **`TransportOutboxDispatcher`** (Core, internal) — turns a `MessageKind.Contract` row into an
  envelope and hands it over. Registered by the new **`MessagingBuilder.AddTransportDispatcher()`**.

**`SendAsync` returning means the destination acknowledged the message.** Not that it was enqueued,
buffered, or fired and forgotten. `OutboxProcessor` marks the row `Published` on that return and
`Published` is terminal, so a transport that hands off asynchronously turns the mark into a lie: a
confirmation that never arrives becomes indistinguishable from one that did. RabbitMQ — return after
the publisher confirm; Azure Service Bus — after `SendMessagesAsync`; Kafka — after the delivery
report.

**No implementation of `IMessageTransport` ships in any MicroKit package**, and that is deliberate
rather than an omission. An in-process transport is meaningless until the receiving seam exists, and
one that returned successfully with nowhere to deliver would be the silent-success failure this
module treats as blocking. A broker provider supplies the implementation. Pinned by
`NoMicroKitPackageShipsAnIMessageTransportImplementation`, which asserts all four shipped assemblies
— Abstractions, Core, EntityFrameworkCore and the MediatR glue — because that is the claim being
made.

**With no transport registered, a contract row fails loudly and reversibly.** The dispatcher takes
`IMessageTransport` through its constructor, so the container fails while `OutboxProcessor` is
resolving the dispatcher — which that processor converts into `OutboxConfigurationException`. The
batch is released untouched, no retry budget is consumed, the rows stay `Pending`, and the worker
stops. This is deliberately not a startup validation: whether a transport is *needed* depends on
whether any contract row exists, which is data rather than composition, so a host publishing only
notifications must compose without one and must not fail at boot.

#### Conformance obligation — owed by the first broker provider

The acknowledgement rule above cannot be enforced from this repository: no test here can observe
whether somebody else's `SendAsync` returned before its broker confirmed. It is therefore recorded
as a debt rather than left to be rediscovered.

> **The first broker provider owes a conformance test asserting that `SendAsync` does not return
> before the broker has acknowledged the message. A transport that fails it is unusable** —
> regardless of whether it compiles, and regardless of what its other tests show.

The consequence of not paying it is not a degraded transport but a silently lying one: every
`Published` mark in the producing system becomes false, and nothing downstream can detect it. No
conformance harness ships today because there is no provider to run one against, and one written
blind would only be designed twice. The obligation is repeated in `IMessageTransport`'s XML
remarks, which ship inside the package.

#### The envelope is a compatibility commitment — at the member level

Once a message has travelled, every member is something a deployed consumer must still read a year
later — including one built by someone who does not share this repository. Removing or renaming one
breaks every deployed consumer at once.

**What this package does not own is the byte encoding**, and the distinction is worth stating before
the first message travels rather than after. Nothing in MicroKit serializes an envelope:
`TransportOutboxDispatcher` hands the object to `IMessageTransport.SendAsync`, and the provider
behind that seam decides how it reaches the broker. Property casing, whether a null member is
written or omitted, and the `DateTimeOffset` format are a provider's decisions. So the member set is
what this type guarantees; the bytes are what a provider guarantees.

> **The first broker provider owes that decision explicitly** — casing, null omission, timestamp
> format — rather than inheriting whatever its serializer happens to default to. **Once one ships,
> its encoding is the de-facto standard** and every later provider matches it. A second provider
> that chooses differently splits the wire format in two while every member name still agrees, which
> is the harder version of this problem: nothing about the C# type looks wrong.

No canonical encoder ships today, deliberately — there is no provider to use one, and one written
with nothing to exercise it would only be designed twice.

| Member | Type | Why it travels |
|---|---|---|
| `MessageId` | `Guid` | The consumer's idempotency key. The **producing row's** id, so it is identical across every redelivery |
| `ContractName` | `string` | The only identity the receiving process can act on |
| `Source` | `string` | The emitting module — routing, provenance, and `(source, id)` uniqueness |
| `Payload` | `string` | The JSON, opaque, byte for byte |
| `TenantId` | `string?` | A consumer in a shared-database deployment cannot process a message without it |
| `CorrelationId` / `CausationId` | `Guid?` | The trace chain, at the boundary where it is least reconstructible |
| `OccurredOnUtc` | `DateTimeOffset` | The business timeline, not the relay's clock |

Deliberately absent: **`EventType`** (an assembly-qualified name is meaningless in the receiving
process — shipping it would invite `Type.GetType`, which works in a monolith and fails on
extraction), **`SourceMessageId`** (the producer's internal replay key), **a trace parent** (the
right thing to propagate, but `OutboxMessage` carries no such column yet, and a member that could
only ever be null is worse than an absent one), delivery bookkeeping, and an envelope version.

`Payload` being a `string` **commits the wire to a text payload**, recorded here as an accepted
constraint rather than left implicit: `IMessageSerializer.Serialize` returns a string, so the row
already holds text, and a JSON body is what every broker this module targets carries without
ceremony. A binary format later means base64 into that member or a second member beside it.

#### How a member is added to `MessageEnvelope`

Decided now, because `TraceParent` is already anticipated and the first addition would otherwise
make this decision by accident. **The primary constructor signature is frozen.** A new member is
declared as an `init` property in the record body, with a default:

```csharp
public sealed record MessageEnvelope(/* … unchanged … */)
{
    public string? TraceParent { get; init; }
}
```

Adding a parameter to the primary constructor instead would be source- *and* binary-breaking for
every `new MessageEnvelope(…)` call site — which is exactly where an inbound receiver builds one
from a broker message — and would change the deconstructor's arity with it. "Additive" holds on the
wire, where a consumer that does not know a member ignores it; it does not hold for a public
positional record, and conflating the two is how the first addition becomes a break. `with`
expressions, value equality and `System.Text.Json` treat a body-declared `init` property exactly as
they treat a positional one.

**The identifiers are `Guid`s, not the `MessageId` / `CorrelationId` / `CausationId` records used
everywhere else.** Those are positional wrappers, so a reflection-based serializer emits them as
`{"value":"…"}` — a C# detail a non-.NET consumer would read as `envelope.messageId.value` forever.
A `JsonConverter` was rejected because it would push the decision down into serializer
*configuration*, where a provider registering its own serializer, or a swap to the planned
source-generated one, silently drops it. The declared member type is the one part of the encoding
this package can fix from here, which is why it is the part worth spending: casing and null handling
belong to a provider, the shape of an identifier does not. Pinned by
`MessageEnvelope_SerializesToTheDeclaredWireShape` and
`MessageEnvelope_CarriesIdentifiersAsBareStrings`.

#### Routing, and why a notification is a configuration fault

| Row | Response |
|---|---|
| `Contract`, valid | envelope → `IMessageTransport.SendAsync` |
| `Contract`, no `ContractName` or `Source` | `OutboxPayloadException` — unaddressable, dead-letter |
| `Notification` | **`OutboxConfigurationException`** |
| unknown `MessageKind` | `OutboxPayloadException` |

A notification reaching the standard dispatcher is not a bad payload — the row is fine and would
dispatch correctly the moment `AddMediatRDomainEvents()` is called. It is a missing registration.
The classification decides whether a one-line omission in a composition root is recoverable:
`OutboxPayloadException` dead-letters on *first sight*, so it would silently dead-letter every
domain event in the system on the first poll, recoverable only by operator requeue.
`OutboxConfigurationException` releases the batch untouched and stops the worker, so the rows
survive and deploying the missing package drains them.

An **unknown** kind is the opposite, and the asymmetry is deliberate: `MessageKind` is persisted as
a string, so a later build or a hand edit can produce a value this build cannot interpret at all.
That is permanent for this deployment, so it dead-letters.

#### `OutboxConfigurationException` now has three origins, and the diagnostics say so

Before this release it had one: `IOutboxDispatcher` was not registered. It now has three, and the
messages an operator reads first were still describing only the first — which sends them to verify a
registration that is already in their composition root.

| Origin | Was reported as | Now |
|---|---|---|
| `IOutboxDispatcher` unregistered | "IOutboxDispatcher is not registered" | unchanged in substance |
| A **dependency** of a registered dispatcher unregistered — a transport dispatcher with no `IMessageTransport`, the **likeliest of the three** once `AddTransportDispatcher()` is composed | same text, which is false | names `IMessageTransport` and points at the inner exception, which carries the type the container could not supply |
| A dispatcher handed a row it structurally cannot serve — a `Notification` with no MediatR glue | same text, which is false | the dispatcher's own message already named `AddMediatRDomainEvents()`; the log headline no longer contradicts it |

`OutboxProcessorLogs.DispatcherUnresolvable` is renamed **`DispatchMisconfigured`** (internal to
Core) and its message names no particular registration, deferring to the exception it is logged
with. `EventId` 1006 is unchanged, so log-based alerting keyed on it is unaffected.

The test that pinned the old text now pins `IMessageTransport` instead: an assertion holding
misleading text in place is worse than no assertion, because it makes correcting the message look
like breaking a contract.

#### Which layer may raise `OutboxConfigurationException`

`IMessageTransport`'s remarks forbade raising it "from here" on the grounds that the classification
belongs to service *resolution* — a rule `TransportOutboxDispatcher` itself does not follow, since it
raises the exception from a dispatch for a `Notification` row. The design is right; the doc was
stale, and read as written it would tell a provider author the classification is reserved to the
processor.

Stated correctly now, on both the interface and the exception: **a transport must never raise it**,
because by the time a transport runs the composition has already been proven adequate by the fact
that the transport was resolved and called. **A dispatcher may**, for a row it structurally cannot
serve in this composition. Both engine origins establish the fault before any delivery is attempted,
which is what distinguishes them from a runtime failure.

#### Nothing deserializes on the send path

`TransportOutboxDispatcher` takes no `IMessageSerializer` and no `IntegrationEventRegistry`. The
payload was serialized by this same process at staging, so materializing it would resolve a type,
build an object and re-serialize it to identical bytes — while making the send path depend on the
producer's type graph, which is precisely what does not cross a boundary. It would also make a
message unsendable whenever its CLR type moved assemblies between staging and dispatch, though the
bytes on the wire were fine. The receiver resolves the contract name to its own local type.

One consequence, stated so it is not met during an incident: this dispatcher **cannot detect a
malformed payload**, where `InProcessIntegrationDispatcher` can. A corrupt payload travels and
dead-letters at the consumer, in the consumer's inbox — the correct place, since the receiver is the
party that knows what the name should deserialize into, but it means a producer-side operator can
see a healthy queue during a consumer-side incident.

### Changed — BREAKING: `MessageEnvelope<T>` → `MessageEnvelope`

The generic envelope is replaced by a non-generic record. It had zero references anywhere — nothing
constructed, consumed or transmitted one — so the break is nominal, but it is an arity change on a
public type and is recorded as one.

The old shape was unusable as a wire format: it was generic over `T : IIntegrationEvent` and carried
a *deserialized instance*, so a sender holding JSON would have had to materialize an object purely
to re-serialize it, and `IMessageTransport` would have had to be generic or box. It had **no
`ContractName`** at all — the disqualifying omission, since without one a receiver has nothing to
resolve. Its `TenantId` was non-nullable, contradicting `OutboxMessage.TenantId` and ADR-MSG-008 §5.

### Added — `OutboxMessage.Source`

The emitting module, e.g. `/shop/orders`, mirroring `IntegrationEventMessage.Source` and its width.
Null on a notification row; `TransportOutboxDispatcher` enforces non-null on the contract path,
where the entity cannot.

It lands now rather than with the publisher because the envelope should ship complete **before the
first message travels**, and no contract row exists yet — the same "the table is empty" argument
that justified the `MessageKind` / `ContractName` / `SourceMessageId` columns. The publisher needs
the column regardless, so this is one migration instead of two.

**Stamped at staging, never resolved at dispatch.** Reading it from `IntegrationEventRegistry` when
the message is sent looks equivalent and is not: it would make the emitted source a function of the
composition running *now* rather than of what was staged, so a row staged before a rename would
travel under the new name. That is the defect ADR-MSG-018 removed by sourcing every field from the
row.

**Migration** (PostgreSQL / SQLite):

```sql
ALTER TABLE "OutboxMessages" ADD COLUMN "Source" varchar(256) NULL;
```

### Changed — `[IntegrationEvent]` rejects a blank contract name

`IntegrationEventAttribute` now calls `ArgumentException.ThrowIfNullOrWhiteSpace(contractName)`.
The guard sits on the attribute rather than in either builder, so one check covers publication and
consumption alike, and it fires inside the registration call that names the offending type.

The empty name is the case this is for, more than the null one. Null fails somewhere regardless; an
empty string is a perfectly usable dictionary key. Unguarded it bound, resolved, and travelled — an
event published under no wire identity at all, which a consumer could match only by having made the
same mistake. Note the value guarded here is the more consequential of the two on that call: `source`
was already validated this way.

### Fixed
- **Lost update under lease expiry.** `MarkPublishedAsync` / `MarkFailedAsync` / `DeadLetterAsync`
  filtered on `m.Id` alone, so a processor whose lease expired mid-dispatch silently overwrote the
  processor that legitimately re-claimed the message. Every terminal write now also filters on
  `ClaimToken`; a lost lease yields zero rows.
- **Silent success.** Those methods returned `Result.Success()` unconditionally, discarding the
  affected-row count. `ApplyOutcomesAsync` returns the row count and the processor logs a partial
  settlement; `RequeueAsync` returns `bool`.
- **Retention unreachable in single-tenant deployments.** `DeleteProcessedAsync` and
  `GetDeadLetteredAsync` took a non-nullable `tenantId` compared against a nullable `TenantId`, so
  rows with a null tenant never matched and the outbox grew without bound. Both now take `string?`,
  with `null` meaning every tenant.
- **Retention had no caller at all.** `DeleteProcessedAsync` was invoked by nothing and
  `RetentionDays` was read by nothing. `OutboxRetentionWorker` now runs one pass on a slow timer.
- **A missing `IOutboxDispatcher` registration burned the whole queue's retry budget.** It threw per
  message and was classified transient. It is now caught structurally as
  `OutboxConfigurationException`: the batch is settled with every message released, then the fault
  is rethrown and the worker stops.
- **Poison messages consumed the full retry budget** for a verdict reached on the first attempt.
  `OutboxPayloadException` dead-letters on first sight.
- **A broker outage failed all N messages, wrote N failure rows and consumed N retry budgets**, then
  repeated next tick. `OutboxTransportUnavailableException` abandons the batch and releases the
  remainder as `Released` — no retry consumed.
- **`Action<OutboxProcessorOptions>` was a silent no-op.** `OutboxProcessorOptions` had `init`-only
  accessors, so the configuration callback taken by `AddMicroKitMessaging` could not assign
  anything. Accessors are now `{ get; set; }`.
- **Deterministic back-off caused synchronised retries.** With several processor instances, messages
  that failed together retried together. Back-off is now
  `Uniform(0, min(2^RetryCount s, MaxRetryBackoff))` — full jitter.

### Changed — BREAKING (MicroKit.Messaging.MediatR)
- **`AddMediatRTransport()` is renamed `AddMediatRDomainEvents()`, with no `[Obsolete]` alias.** The
  old name was wrong twice: it transports nothing, and `Add{Provider}Transport()` is reserved by this
  module's naming rules for broker providers (`AddRabbitMqTransport`). The method contributes the
  outbox `IDomainEventsSink`, decorates `IOutboxDispatcher`, and replaces `INotificationPublisher` —
  three things, none of them a transport. Both packages are `1.0.0-preview.*` with zero external
  consumers, so the rename ships outright rather than accumulating a permanent alias.
  **Migration:** rename the call. Nothing else changes — same receiver, same signature, same
  position in the chain (ADR-MEDIATR-015).
- **The glue no longer registers an `IDomainEventsDispatcher`.** It contributes an
  `IDomainEventsSink` to the single core orchestrator in `MicroKit.MediatR` instead of registering a
  rival dispatcher, so the two packages can no longer disagree about which implementation wins —
  the race is gone rather than arbitrated. `DomainEventsDispatcher` becomes `OutboxDomainEventSink`
  and sheds the drain and handler-dispatch phases it used to duplicate; both types are
  `internal sealed`, so **no consumer-visible type changed**. **This requires MicroKit.MediatR from
  the same release** — the glue will not compile against a core without `IDomainEventsSink`
  (ADR-MEDIATR-014).

### Fixed
- **`AddInProcessTransport()` called after the MediatR glue silently disabled notification
  publishing.** It registered `IOutboxDispatcher`, `IMessagePublisher` and `IMessageSerializer` with
  a plain `Add`, so a later call appended a second `IOutboxDispatcher` descriptor; Microsoft DI
  resolves the last one, and `MediatROutboxDispatcher` was bypassed with no exception and no log.
  The outbox kept draining and reporting success while every domain-event notification went
  unpublished. All three registrations now use `TryAdd` — a transport supplies a default and
  abstains if something already holds the slot. The duplicate `IMessageSerializer` descriptor that
  stacked for the same reason is gone too.
- **Calling the MediatR glue twice double-wrapped the outbox dispatcher.** Its `LastOrDefault`
  descriptor hunt found its own factory registration on the second call and decorated it again,
  routing every outbox message through two decorators. The method is now idempotent: one sink
  (`TryAddEnumerable`), one decorator, one publisher replacement, however many times it is called
  (ADR-MEDIATR-015).

### Added
- `MicroKit.Messaging/README.md` — the module had none. Covers what the packages do, the complete DI
  composition that works, one end-to-end example, and what is stable versus still moving.
- `OutboxClaim`, `OutboxOutcome` + `OutboxOutcomeKind`, `OutboxBatchResult` + `OutboxBatchAbortReason`.
- `OutboxPayloadException`, `OutboxTransportUnavailableException`, `OutboxConfigurationException`.
- `IOutboxAdminStore` and `IOutboxRetentionStore` — split out of `IOutboxProcessorStore` (ISP).
- `OutboxMessage.ClaimToken` (`Guid?`), plus `IX_OutboxMessages_Dispatchable` and
  `IX_OutboxMessages_ClaimToken`.
- `IComparable<T>` on `MessageId`, `CorrelationId` and `CausationId` — the claim sorts candidate ids
  for deterministic lock ordering, and a positional record derives no ordering.
- Adaptive worker cadence, driven by `OutboxBatchResult`.
- `OutboxRetentionWorker`.
- `OutboxProcessorOptions`: `MaxPollingInterval`, `TransportUnavailableBackoff`, `MaxRetryBackoff`,
  `OutcomeFlushTimeout`, `MaxErrorMessageLength`, `RetentionInterval`.
- `TimeProvider` and `Random` registered via `TryAddSingleton`, injected into the processor and
  store so back-off is testable without a wall clock.

### Fixed — inbox

- **A redelivery dead-lettered a correctly delivered message.** `IInboxStore.AddAsync` reported its
  *nominal* outcome — "already present, nothing done" — by throwing. Under at-least-once delivery a
  redelivery needs no failure at all; one expired lease after a crash produces it. Nothing caught
  the exception, so it reached `OutboxProcessor`, was classified a transient dispatch failure, and
  retried into the same duplicate until the message dead-lettered. `IInboxWriter.AddAsync` now
  returns `InboxWriteResult`, and `InProcessMessagePublisher` treats `AlreadyPresent` as a
  successful skip.
- **A duplicate on one consumer silently lost the rows of every later consumer.** One event fans out
  to one row per consumer; the escaping exception ended the whole publish, so consumers 3..N never
  got their row at all — a partial redelivery becoming permanent loss. The publisher now
  `continue`s.
- **`MarkProcessingAsync` was not a lease.** No eligibility predicate, `void` return: an
  unconditional write. Two processors could both "acquire" the same row, both invoke the handler,
  and neither could tell — in the component whose entire job is deduplication. Replaced by
  `ClaimBatchAsync`, which replays the eligibility predicate inside a single `UPDATE`.
- **Lost update on every terminal inbox write.** `MarkProcessedAsync` / `MarkFailedAsync` /
  `DeadLetterAsync` filtered on the compound key alone, so a processor whose lease had expired
  overwrote the one that legitimately re-claimed the row. Every write now filters on `ClaimToken`.
- **Silent success.** All three returned `Result.Success()` regardless of affected rows.
  `ApplyOutcomesAsync` returns the row count and the processor logs a partial settlement;
  `RequeueAsync` returns `bool`.
- **A missing handler registration killed the whole batch.** Handler resolution sat outside the
  `try`, so it threw straight out of `ProcessBatchAsync`: every lease stranded until expiry, and the
  worker classified it transient and retried forever. It is now caught structurally as
  `InboxConfigurationException`; the batch is settled with every row released, then the fault is
  rethrown and the worker stops.
- **Two permanent failures burned the full retry budget.** An unregistered `ConsumerType` and a
  payload that will not deserialize each took `MaxRetries` attempts with exponential back-off for a
  verdict fixed at the first. Both now raise `InboxPayloadException` and dead-letter on first sight.
- **An unknown consumer settled an unleased row.** The registry check ran before any lease was
  taken, yet the failure policy wrote anyway. The check now runs inside the claimed batch, so every
  write is under a live claim.
- **A downstream outage failed all N rows, wrote N failure rows and consumed N retry budgets**, then
  repeated next tick. `InboxDependencyUnavailableException` abandons the batch and releases the
  remainder as `Released` — no retry consumed.
- **`AddAsync` poisoned the context on a duplicate.** The rejected entity stayed tracked as added, so
  the next `SaveChangesAsync` retried the same failing insert — one duplicate broke every later
  write on that context. It is detached before rethrowing.
- **Inbox retention was unreachable and had no caller.** There was no `DeleteProcessedAsync` at all,
  so the table grew without bound. `IInboxRetentionStore` and `InboxRetentionWorker` now exist, and
  `GetDeadLetteredAsync` takes `string?` so a single-tenant deployment — where every row has a null
  tenant — matches.
- **`Action<InboxProcessorOptions>` was a silent no-op**, the same `init`-only defect the outbox
  options carried. Accessors are now `{ get; set; }`.
- **The transactional guarantee silently did not exist under `QueryTrackingBehavior.NoTracking`.**
  `StageProcessedAsync` did not state its tracking mode — the one query in `EfInboxStore` that
  needs tracking, while the other four all say `AsNoTracking()`. On a consumer `DbContext` with
  that ordinary global setting the row came back untracked, the mark reached nothing, and
  `IsMarkUncommitted` reported it committed: the batch returned `Processed` while the row kept its
  claim token and replayed on every pass, never incrementing `RetryCount` and never
  dead-lettering. Now `AsTracking()`, and pinned by `InboxSettlementGuaranteeTests`.
- **`IsMarkUncommitted` read a discarded mark as a committed one.** An absent or detached entry
  was treated as "nothing pending, therefore saved" — literally true and operationally wrong: a
  handler calling `ChangeTracker.Clear()` throws the staged mark away, and nothing is pending
  precisely because the write was discarded. Absent now means uncommitted, so the failure costs a
  redundant deferred write and a warning instead of a silent infinite replay.
- **The inbox had no jitter at all.** Back-off is now
  `Uniform(0, min(2^RetryCount s, MaxRetryBackoff))`, computed by the processor rather than the
  store so it is testable without a database.

### Added — inbox

- `InboxClaim`, `InboxOutcome` + `InboxOutcomeKind`, `InboxBatchResult` + `InboxBatchAbortReason`,
  `InboxMessageKey`, `InboxWriteResult`.
- `InboxPayloadException`, `InboxDependencyUnavailableException`, `InboxConfigurationException`.
- `IInboxWriter`, `IInboxProcessorStore`, `IInboxSettlementStore`, `IInboxAdminStore`,
  `IInboxRetentionStore` — `IInboxStore` split five ways (ISP).
- `InboxMessage.RowId` (`Guid`, the new primary key) and `InboxMessage.ClaimToken` (`Guid?`), plus
  `UX_InboxMessages_MessageId_ConsumerType`, `IX_InboxMessages_Processable`,
  `IX_InboxMessages_ClaimToken` and `IX_InboxMessages_TenantId_ProcessedAt`.
- Adaptive inbox worker cadence, driven by `InboxBatchResult`.
- `InboxRetentionWorker`, and `InboxMetrics` (`microkit.inbox.messages.added` /
  `.deduplicated`; subscribe with `AddMeter(InboxMetrics.MeterName)`).
- `InboxProcessorOptions`: `MaxPollingInterval`, `DependencyUnavailableBackoff`, `MaxRetryBackoff`,
  `OutcomeFlushTimeout`, `MaxErrorMessageLength`, `RetentionDays`, `RetentionInterval`.
- `TimeProvider` and `Random` injected into `InboxProcessor`; `TimeProvider` into
  `EfInboxStore<TContext>`.

### Changed — BREAKING (inbox)

1. `IInboxStore` is **removed**, split into `IInboxWriter`, `IInboxProcessorStore`,
   `IInboxSettlementStore`, `IInboxAdminStore` and `IInboxRetentionStore`.
2. `IInboxWriter.AddAsync` returns `ValueTask<InboxWriteResult>` instead of `ValueTask`.
   **This breaks fewer callers than it looks.** `await writer.AddAsync(message, ct);` compiles
   unchanged — discarding the value of an awaited expression used as a statement is legal C# and
   raises no diagnostic, even under `TreatWarningsAsErrors`. Only a caller that assigned the
   returned `ValueTask` to a variable, or converted it with `.AsTask()`, has to change.
   So **nothing in the type system stops you ignoring the result**, and an earlier draft of this
   note claimed otherwise. If you ignore it, a rising deduplication rate — the signal of a lease
   set too short or a consumer stalling — becomes invisible in your logs. The enforcement that
   actually exists is `InboxMetrics`: `microkit.inbox.messages.deduplicated` is recorded on the
   ingestion path whether or not the caller inspects the return value. Subscribe to it.
3. `IInboxProcessor.ProcessBatchAsync` returns `ValueTask<InboxBatchResult>` (ADR-MSG-017).
4. `IInboxCoordinator.ExecuteAsync` returns `ValueTask<InboxBatchResult>` (ADR-MSG-017). Together
   with (3) this closes the inbox half of ADR-MSG-014, which ADR-MSG-015 left dated as debt.
5. `InboxProcessor` and `EfInboxStore<TContext>` take new constructor dependencies
   (`TimeProvider`, and `Random` for the processor).
6. `InboxMessage` gains `RowId` and `ClaimToken`, and **the primary key moves to `RowId`** — see
   the migration below, which is not optional.
7. `InboxProcessorOptions` — `init` accessors become `set`.
8. **`BatchSize` 20 → 100 and `MaxRetries` 10 → 5 are behavioural changes**, not merely new
   defaults. Both bind unchanged from existing configuration but halve the retry budget.
9. `InboxProcessorOptions.RetentionDays` defaults to **30, not the outbox's 7, and the asymmetry is
   deliberate.** On the outbox, deleting early loses history; on the inbox it loses the
   deduplication guarantee, because the table only deduplicates messages it still holds. The window
   must exceed the maximum plausible redelivery delay of every upstream transport.

### Migration — inbox schema

**Two structural changes, not one: the claim token *and* the primary key.** A consumer who applies
only `claim_token` gets a schema the code cannot query.

**Drain the inbox before migrating.** Stop the workers and let the table empty. Rows sitting in
`Processing` when the primary key changes are the one case with no clean answer. On a table of any
size steps 1 and 3 take an `ACCESS EXCLUSIVE` lock.

```sql
-- 1. Surrogate key. Populate existing rows before making it NOT NULL.
ALTER TABLE inbox_messages ADD COLUMN row_id uuid;
UPDATE inbox_messages SET row_id = gen_random_uuid() WHERE row_id IS NULL;
ALTER TABLE inbox_messages ALTER COLUMN row_id SET NOT NULL;

-- 2. Recreate the dedup gate BEFORE dropping the primary key, so the table is never left
--    without one. Dropping first would leave a window, however short, in which duplicate
--    ingestion is accepted. A redundant unique index alongside the PK is allowed.
CREATE UNIQUE INDEX ux_inbox_messages_message_id_consumer_type
    ON inbox_messages (message_id, consumer_type);

-- 3. Swap the primary key. Confirm the existing constraint name first:
--      SELECT conname FROM pg_constraint
--      WHERE conrelid = 'inbox_messages'::regclass AND contype = 'p';
ALTER TABLE inbox_messages DROP CONSTRAINT pk_inbox_messages;
ALTER TABLE inbox_messages ADD  CONSTRAINT pk_inbox_messages PRIMARY KEY (row_id);

-- 4. Claim token.
ALTER TABLE inbox_messages ADD COLUMN claim_token uuid NULL;
CREATE INDEX ix_inbox_messages_claim_token ON inbox_messages (claim_token);

-- 5. Claim path, ordered by received_at_utc.
CREATE INDEX ix_inbox_messages_processable
    ON inbox_messages (dead_lettered, status, next_retry_at_utc, received_at_utc);

-- 6. Retention.
CREATE INDEX ix_inbox_messages_tenant_id_processed_at
    ON inbox_messages (tenant_id, processed_at_utc);
```

> The EF Core configuration ships **unfiltered** indexes because `HasFilter` takes
> provider-specific SQL and `MicroKit.Messaging.EntityFrameworkCore` is the provider-neutral
> package. A consumer writing their own DDL should prefer the partial forms
> (`WHERE claim_token IS NOT NULL`, `WHERE dead_lettered = false`).

### Changed — BREAKING (outbox)
1. `IOutboxProcessorStore` — five methods replaced by `ClaimBatchAsync` + `ApplyOutcomesAsync`;
   three moved to `IOutboxAdminStore` / `IOutboxRetentionStore`.
2. `IOutboxProcessor.ProcessBatchAsync` — returns `ValueTask<OutboxBatchResult>` (ADR-MSG-015).
3. `IOutboxCoordinator.ExecuteAsync` — returns `ValueTask<OutboxBatchResult>` (ADR-MSG-015).
4. `OutboxProcessor` and `EfOutboxStore<TContext>` — new `TimeProvider` (and, for the processor,
   `Random`) constructor dependencies.
5. `OutboxMessage` — new `ClaimToken` property; requires
   `ALTER TABLE ... ADD COLUMN claim_token uuid NULL` for consumers who own their DDL.
6. `OutboxProcessorOptions` — `init` accessors become `set`.
7. **`BatchSize` 20 → 100 and `MaxRetries` 10 → 5 are behavioural changes, not merely new
   defaults.** Both bind unchanged from existing configuration but halve the retry budget.
8. `MediatROutboxDispatcher` and `InProcessIntegrationDispatcher` throw `OutboxPayloadException`
   instead of `InvalidOperationException` on an unresolvable or malformed payload.

### Added — integration event publishing

The first half of the integration-event path: a notification handler states that a domain fact
becomes a published contract, and the row is staged durably in the handler's own transaction.
Nothing delivers it yet — the relay, the envelope and the broker adapters are the transport lot.

See ADR-MSG-018.

- **`IIntegrationEventPublisher`** — `PublishAsync<TEvent>(evt, occurredOnUtc, ct)` returns the
  assigned `MessageId`. It **stages and never commits**: if the caller's transaction rolls back,
  the event was never published. Same contract `IOutboxWriter.AddAsync` has one transaction
  earlier.
- **`[IntegrationEvent("name.v1")]`** — mandatory on every published event. The CLR type name
  cannot serve as a wire identity: a consumer in another service holds a different type in a
  different assembly, and a namespace rename would invalidate rows already in flight.
- **`IntegrationEventMessage`** and its own table. Metadata lives in columns so a claim can filter
  and order without parsing JSON. Two timestamps, deliberately: `CreatedAtUtc` (staging, always
  set, what a claim orders on) and `OccurredOnUtc` (the business fact, optional). Ordering on the
  business timestamp would let a backdated event jump the queue.
- **Per-module registration.** `AddIntegrationEventContracts(source, …)` once per module,
  `AddIntegrationEventPublishing()` once per application, `AddEfCoreIntegrationEvents<TContext>()`
  for the staging writer. The `source` is stored per registration, not in a shared singleton that
  the last module to register would overwrite for all the others. Composing every module into one
  registry is also what makes a cross-module contract-name collision detectable at all.
- **Startup validation.** A duplicated contract name fails at boot, not on the one code path that
  emits that event — where it would roll back a transaction and be retried forever as a transient
  failure.

**A handler must publish inside a transaction it opened, and `CommitAsync` alone is not that.**
`IUnitOfWork.CommitAsync` is a bare `SaveChangesAsync` running under the provider's implicit
per-call transaction, which never appears as an open one. Publishing therefore belongs inside
`ITransactionalContext.ExecuteAsync`; `IntegrationEventPublishException` says so at the point of
failure, and `IIntegrationEventPublisher` carries a worked example.

### Fixed

- **`IExecutionContext` reached constructor injection for the first time.** The per-message
  execution scope exposed the message-row context by wrapping the scope's `IServiceProvider`, and
  that wrapper is consulted only for a direct `GetService` call — Microsoft DI activates
  constructor dependencies from its own scope and never sees it. Every service taking
  `IExecutionContext` as a constructor parameter therefore received the default registration: a
  fresh `CorrelationId` with a null `TenantId`. Cascade outbox rows written by
  `OutboxDomainEventSink` lost their tenant and had their correlation chain severed at exactly the
  hop this module exists to preserve. `IExecutionContext` now resolves through a scoped
  `ExecutionContextHolder` that the scope factory populates, so constructor injection sees the
  message row. The `PassThroughExecutionScopeFactory` docs, which described the old behaviour as a
  permanent defect, are corrected.

### Changed — BREAKING (in-process transport withdrawn)

- **`IMessagePublisher` and `InProcessMessagePublisher` are deleted.** The fan-out they performed —
  one `InboxMessage` per registered consumer — moves into `InProcessIntegrationDispatcher`, with
  **every field taken from the `OutboxMessage` row** rather than from the event instance.
  Behaviour is unchanged; the metadata source is not.

  This closes a latent bug. The inbox deduplicates on `(MessageId, ConsumerType)`, and that
  `MessageId` must be stable across redeliveries of the same outbox row. Reading it off the
  deserialized event survived that only by accident: the same payload happens to deserialize to
  the same value, but nothing guaranteed it — not the contract, not the serializer, and not an
  event type free to compute its identity in a property initializer.

  **Migration:** none for consumers who compose with `AddInProcessTransport()`, which keeps its
  `IMessageSerializer` and `IOutboxDispatcher` registrations. A consumer who implemented
  `IMessagePublisher` themselves has no replacement in this release; the transport seam arrives
  with the transport libraries.

- **`IIntegrationEvent` loses every member and becomes a bare marker**, keeping its `IEvent` base.
  `MessageId`, `TenantId`, `CorrelationId`, `CausationId` and `OccurredOnUtc` are gone. They
  existed only because `IMessagePublisher` had no other metadata source, and every one of them
  also existed as a column on the message row with nothing keeping the two in agreement — a
  discrepancy nothing detects, on the field that governs isolation.

  **Migration:** delete those members from your event types. They are inert the moment the
  interface stops declaring them, so the compiler will point at each one (CA1822 on any that were
  expression-bodied). Metadata is assigned at staging from the ambient `IExecutionContext`.

  Both packages are `1.0.0-preview.*` with no external consumers, so both breaks ship outright
  rather than accumulating a permanent shim. After `1.0.0` neither is available at any reasonable
  cost.

### Migration — integration event schema

One new table. Nothing existing changes, so this is additive and needs no downtime.

Consumers who call `modelBuilder.ApplyMessagingConfiguration()` and generate their own EF
migrations need no manual step — the configuration is applied automatically. The DDL below is for
consumers who own their schema, and is the script EF Core emits for this model on PostgreSQL,
reproduced verbatim rather than transcribed.

```sql
CREATE TABLE "IntegrationEventMessages" (
    "Id" uuid NOT NULL,
    "ContractName" character varying(256) NOT NULL,
    "Source" character varying(256) NOT NULL,
    "Data" text NOT NULL,
    "TenantId" character varying(256),
    "CorrelationId" uuid,
    "CausationId" uuid,
    "TraceParent" character varying(64),

    -- Two timestamps, deliberately. CreatedAtUtc is when the row was staged and is always set;
    -- OccurredOnUtc is when the fact happened and is null when the caller did not say. A claim
    -- orders on the first — ordering on the second would let a backdated event jump the queue
    -- and make queue order depend on caller-supplied data.
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "OccurredOnUtc" timestamp with time zone,

    -- Status answers "where is this now"; DeadLettered answers "was it ever given up on".
    -- Orthogonal on purpose: a single Failed status would lose the second answer the moment a
    -- requeue moved the row back to pending. A dead-lettered row reads Pending + flag set.
    "Status" character varying(32) NOT NULL,
    "DeadLettered" boolean NOT NULL,

    "RetryCount" integer NOT NULL,
    "NextRetryAtUtc" timestamp with time zone,
    "LockedUntilUtc" timestamp with time zone,
    "ClaimToken" uuid,
    "ProcessedAtUtc" timestamp with time zone,
    "ErrorMessage" character varying(2048),
    CONSTRAINT "PK_IntegrationEventMessages" PRIMARY KEY ("Id")
);

-- The claim path: not dead-lettered, pending or past its back-off deadline, oldest staged first.
CREATE INDEX "IX_IntegrationEventMessages_Dispatchable"
    ON "IntegrationEventMessages" ("DeadLettered", "Status", "NextRetryAtUtc", "CreatedAtUtc");

-- Read-back by token, and lease recovery after a crashed relay.
CREATE INDEX "IX_IntegrationEventMessages_ClaimToken"
    ON "IntegrationEventMessages" ("ClaimToken");

-- Retention sweeps delivered rows by age.
CREATE INDEX "IX_IntegrationEventMessages_TenantId_ProcessedAt"
    ON "IntegrationEventMessages" ("TenantId", "ProcessedAtUtc");
```

> The EF Core configuration ships **unfiltered** indexes because `HasFilter` takes
> provider-specific SQL and `MicroKit.Messaging.EntityFrameworkCore` is the provider-neutral
> package. A consumer writing their own DDL should prefer the partial forms
> (`WHERE "ClaimToken" IS NOT NULL`, `WHERE "DeadLettered" = false`).

> ⚠ Identifier casing differs from the inbox migration published above, and the divergence is
> pre-existing rather than introduced here: the shipped EF configuration emits quoted PascalCase
> identifiers, while that earlier SQL block is snake_case. This block matches the code. See
> `L0-FINDINGS.md` #24.

## [1.0.0-preview.4] — 2026-06-27

### Packages Released
- `MicroKit.Messaging.Abstractions`
- `MicroKit.Messaging`
- `MicroKit.Messaging.EntityFrameworkCore`
- `MicroKit.Messaging.MediatR`

### Added
#### MicroKit.Messaging.Abstractions
- `IIntegrationEvent` — typed integration event contract (standalone, no Domain dependency — ADR-MSG-001)
- `IMessagePublisher` — outbound message publishing contract
- `IMessageHandler<T>` — inbound message handling contract
- `IOutboxWriter` — write-side outbox contract (`AddBatchAsync`)
- `IOutboxProcessorStore` — processor-side outbox contract (lease, ack, dead-letter)
- `IInboxStore` — inbox deduplication contract
- `OutboxMessage` / `InboxMessage` — sealed classes (EF Core mutable)
- `MessageEnvelope<T>` — sealed record
- `MessageId`, `CorrelationId`, `CausationId` — strong-typed value objects (sealed record)
- `OutboxMessageStatus` / `InboxMessageStatus` — enums

#### MicroKit.Messaging
- `OutboxMessageFactory` — builds `OutboxMessage` from integration events
- `InProcessMessagePublisher` — in-process synchronous publisher
- `InProcessIntegrationDispatcher` — in-process integration event dispatcher
- `OutboxProcessor` / `InboxProcessor` — polling processors (ADR-MSG-002)
- `IMessageSerializer` / `SystemTextJsonMessageSerializer` — JSON serialization (ADR-MSG-003)
- `IExecutionScopeFactory` integration via `MicroKit.Execution.Abstractions` (ADR-EXEC-001)
- DI extensions

#### MicroKit.Messaging.EntityFrameworkCore
- `EfOutboxStore<TContext>` — `IOutboxWriter` + `IOutboxProcessorStore` (atomic lease via `ExecuteUpdateAsync`, stale-lease recovery, dead-letter — ADR-MSG-002)
- `EfInboxStore<TContext>` — `IInboxStore` (dedup via unique constraint + `DbUpdateException`)
- `OutboxMessageConfiguration` / `InboxMessageConfiguration` — EF Core entity configs
- `ModelBuilderExtensions.ApplyMessagingConfiguration()`
- `MessagingBuilderExtensions.AddEfCoreOutbox<TContext>()`

#### MicroKit.Messaging.MediatR
- `DomainEventsDispatcher` — dispatches domain events to handlers and outbox (ADR-MEDIATR-009)
- `IDomainEventNotificationFactory` — creates `IDomainEventNotification<T>` from domain events
- `IDomainEventHandlerDispatcher` — dispatches to `IDomainEventHandler<T>` (sync, in-transaction)
- MediatR glue for outbox-based async dispatch (P4 pipeline)

### Notes
- `preview.1` through `preview.3` were taken on NuGet.org by a previous implementation
- This is the first release of the new implementation
