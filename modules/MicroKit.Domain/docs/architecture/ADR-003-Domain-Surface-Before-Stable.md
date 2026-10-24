# ADR-003: Settle the Domain Surface Before Stable

## Status
**Accepted** — implemented by issue #160, before `2.0.0`.

## Context

`MicroKit.Domain` is the root of the MicroKit dependency graph. Every other module builds on its
types, and `2.0.0` freezes them: after it, every break costs a major version (ADR-GLOBAL-002
D10). Before it, a known break ships outright on `preview.*`.

A review of `main@6eff5c5` found owed breaks and defects on that surface: duplicated
persistence contracts, an identity guard that does not guard struct identifiers, an ambient
clock in constructors, a nullable creator with several meanings, random GUIDs on ordered keys,
a `Money` type that accepts any three letters, specifications whose predicate is written twice,
and business rules identified only by their CLR type name. Issues #111 to #119 and #160 hold
the findings.

This ADR records the decisions D1 to D9 of #160, with their reasoning and the alternatives
rejected. D10 to D12 (empty shells, documentation, this record) carry no design choice and are
not restated.

## Decisions

### D1 — Repositories leave the Domain package

**Decision.** `src/MicroKit.Domain/Repositories/` is deleted: `IRepository<T, TId>`,
`IReadRepository<T, TId>` and `IUnitOfWork`. `MicroKit.Persistence.Abstractions` owns the
persistence contracts.

**Reasoning.**
- Both packages declared an `IRepository` and an `IUnitOfWork` with the same names and different
  semantics: synchronous without a `CancellationToken` here, asynchronous and implemented by EF
  Core in Persistence (#117, #119). An adopter could not tell which one to use without reading
  both.
- The Domain `IUnitOfWork.Repository<TEntity, TId>()` resolved a repository at run time: a
  service locator in the purest package of the ecosystem (#118).
- Nothing implemented the Domain contracts. A persistence contract is asynchronous I/O, and
  asynchronous I/O is not domain vocabulary: the module rules forbid `Task` in domain methods.

**Rejected alternatives.**
- *Keep the Domain contracts and delete Persistence's.* They would have to become asynchronous
  to be implementable, which brings I/O into the pure package.
- *Keep both under different names.* The ambiguity stays; only the spelling changes.
- *Remove only the service locator.* The duplication stays.

### D2 — Identity is guarded, and `IEntityId` is a marker

**Decision.** `Entity<TId>` throws `ArgumentNullException` for a null identifier and
`ArgumentException` when the identifier equals `default(TId)`. `IEntityId` loses
`object Value` and becomes a marker constraint; the typed `Value` stays on `EntityId<T>` and
`GuidId`. `GuidId.Empty` is deleted.

**Reasoning.**
- The previous guard tested `id is null`, which is never true for a `readonly record struct`:
  `default(TId)` passed and the entity received an empty identity (#111). Struct identifiers are
  the recommended shape, so the uncovered case was the nominal one.
- `object Value` boxed every identifier and erased its type. Nothing outside `MicroKit.Domain`
  read it.
- `GuidId.Empty` exposed the one value the guard now rejects.

**Rejected alternatives.**
- *Validate in each identifier type.* A struct cannot forbid its own `default`; only the
  consumer of the identifier can.
- *Replace `object Value` with a generic `IEntityId<TValue>`.* No consumer needs it today, and
  adding it later is not a break.

### D3 — Entity equality admits subclasses

**Decision.** Two entities are equal when their identifiers are equal and one runtime type is
assignable from the other. `GetHashCode` stays identifier-based.

**Reasoning.**
- An EF Core lazy-loading or change-tracking proxy is a runtime subclass of the mapped entity.
  With exact type equality, an entity and its proxy with the same identifier compared unequal,
  so a tracked instance and a loaded one could not be matched.
- Equal entities share an identifier, so identifier-based hashing stays consistent with
  equality.

**Consequence.** Equality is not transitive across sibling subclasses: an `A` equals a `B : A`
and a `C : A` with the same identifier, while `B` and `C` are unequal. Sibling subclasses
sharing one identifier space is a modelling error this rule does not try to repair.

**Rejected alternatives.**
- *Exact type equality (status quo).* Breaks proxies.
- *Identifier-only equality.* Two unrelated entity types sharing an identifier type, such as
  `GuidId`, would compare equal.
- *Unwrap proxies through EF Core.* Needs an EF Core dependency the package must not have.

### D4 — Time is an input, never an ambient read

**Decision.**
- `AuditableEntity<TId>` and `AuditableAggregateRoot<TId>` each take either an explicit
  `DateTimeOffset createdAt` or a `TimeProvider`, plus the creating `Actor`.
- `DomainEvent` becomes `abstract record DomainEvent(DateTimeOffset OccurredAt)`, with no
  default.
- No `DateTimeOffset.UtcNow` remains under `src/`.
- Every instant entering the domain (`CreatedAt`, `UpdatedAt` through `Touch`, and `OccurredAt`
  on construction and through `with`) is normalised to offset zero with `ToUniversalTime()`, and
  `default(DateTimeOffset)` is rejected with `ArgumentOutOfRangeException`. `Touch` rejects an
  instant earlier than `CreatedAt`; an equal one is accepted (#165). Values EF Core materialises
  through the backing fields bypass the rule; database column mapping is outside this decision.
- `UpdatedAt` never decreases: `Touch` rejects an instant earlier than the current `UpdatedAt`, or
  than `CreatedAt` when the entity was never updated, with `ArgumentOutOfRangeException`; an equal
  instant is accepted (#171). The accepted cost: when a host's nodes' clocks disagree beyond its
  own update cadence, the skew surfaces as an exception, not a silent drift. A node whose clock is
  behind gets it at its own write; a node whose clock is ahead writes successfully, and the
  healthy nodes then get it until real time passes the instant it recorded.

**Reasoning.**
- Reading `DateTimeOffset.UtcNow` in a constructor hides a dependency: tests can only assert a
  range, which flickers in CI, and a stream cannot be replayed at a past date (#112).
- A per-event `UtcNow` default stamps each event at its construction. Two events raised by one
  business call carry different instants, and their order is not guaranteed when the
  milliseconds coincide (#115). The instant belongs to the operation, which passes it to every
  event it raises.
- A positional parameter with no default moves the omission to the compiler and keeps a
  concrete event to one line.
- Once the instant is an input, its representation is the caller's. Normalising preserves the
  instant and only changes the offset, so a correct `DateTimeOffset.Now` stays correct, and a
  provider that stores offset zero only, such as Npgsql for `timestamptz`, accepts it.
  `default(DateTimeOffset)` is an instant that was never set, not a real one, and a modification
  before the creation is an audit trail that cannot be true (#165).

**Rejected alternatives.**
- *A domain `IClock` abstraction.* `TimeProvider` has been in the BCL since .NET 8, with
  `FakeTimeProvider` for tests.
- *A `required init` `OccurredAt` member.* Positional events would have to repeat it in an
  object initializer at every construction site.
- *Stamp events at dispatch.* The instant would record dispatch, not the business fact.
- *Reject a non-zero offset.* Turns a correct `DateTimeOffset.Now` into an error, with no gain
  over normalising (#165).
- *Clamp a backward `Touch` to the current `UpdatedAt`.* It would record an actor against an
  instant that is not when the change happened, and hide the skewed clock instead of surfacing
  it (#171).

### D5 — The creator is an `Actor`, and audit fields have one point of passage

**Decision.**
- `CreatedBy` becomes a non-null `Actor`. `UpdatedBy` is `Actor?`, null until the first update.
- `UpdatedAt` and `UpdatedBy` are `private set` and change only through
  `protected void Touch(DateTimeOffset, Actor)`.
- The built-in actors carry the reserved prefix `$`: `$system`, `$anonymous`, `$migration`.
  Public construction rejects a leading `$`. `Actor.Restore(string)` reads a stored value back,
  returning the built-in instance for a reserved value and rejecting any other `$` value, so a
  value converter can round-trip every actor.
- The two auditable classes keep their duplicated audit code.

**Reasoning.**
- A nullable `string` creator meant, indistinguishably, unknown, background job, migration,
  anonymous and bug (#116). A named actor keeps the audit trail answerable.
- `Actor` wraps an opaque string, so an OIDC `sub`, an Entra object id or a service-account
  name fits without conversion.
- Two independent setters let an aggregate update the timestamp and forget the actor (#113).
  A single member updates both or neither.
- Without a reserved prefix, `Actor.System` and a user whose identifier is `system` would be the
  same value.
- `AuditableAggregateRoot<TId>` must derive from `AggregateRoot<TId>` and `AuditableEntity<TId>`
  from `Entity<TId>`. With single inheritance, sharing the audit code would need a new public
  component type, for about fifteen lines.

**Rejected alternatives.**
- *`string?` (status quo).* See above.
- *A `Guid` user identifier.* Not every identity provider issues GUIDs.
- *Built-ins without a prefix.* They collide with real identifiers.
- *A shared audit component.* A public type for fifteen lines.

### D6 — GUIDs are time-ordered

**Decision.** `DomainEvent.EventId` and `GuidId.New()` use `Guid.CreateVersion7()`.

An event produced by a `with` expression is a different fact and receives a new identity:
`DomainEvent` declares its copy constructor, which copies `OccurredAt` and assigns a fresh
version 7 `EventId`. Only an explicit initializer (`with { EventId = id }`, `new X(…) { EventId = id }`)
preserves an identity, for replay, import and migration. `EventId`'s `init` rejects `Guid.Empty`
with `ArgumentException` (#164).

**Reasoning.**
- A random version 4 GUID fragments the index of a table receiving millions of rows, such as an
  outbox keyed by `EventId`, and `ORDER BY` on it reflects no real order (#114). The same holds
  for primary keys from `GuidId.New()`.
- On PostgreSQL, a version 7 `uuid` sorts by creation time. SQL Server orders
  `uniqueidentifier` by a different byte order, so version 7 is not sequential there, but it is
  no worse than version 4.
- EF Core's `SequentialGuidValueGenerator` only runs for keys EF generates; it never runs for a
  key the domain assigned.
- The compiler-generated copy constructor of a record copies every field, so `evt with { … }`
  produced a new fact under the original `EventId`, and deduplication keyed on it dropped the new
  fact silently. `Guid.Empty` would make every such event collide with every other (#164).

**Rejected alternatives.**
- *`Guid.NewGuid()` (status quo).* See above.
- *A SQL Server-ordered generator.* Provider-specific knowledge in the domain package.
- *Provider-side generation.* Not decided here: the domain assigns identifiers before
  persistence, because events reference them.

### D7 — `Money` knows its currency

**Decision.**
- A `Currency` value type holds an ISO 4217 code and its minor unit, from an embedded table of
  the active codes of ISO 4217 List One. The table cites its edition date.
- `Money` holds a `Currency`. Construction rejects an unknown code and an amount with more
  decimals than the minor unit allows.
- `Multiply(decimal factor, MidpointRounding rounding)` takes no default rounding mode.
- `Allocate(params int[] ratios)` splits an amount without losing a minor unit: remainders go
  to the first shares.
- `ToString()` uses the invariant culture and the currency's minor unit.

**Reasoning.**
- Any three letters were accepted as a currency, and every amount was formatted with two
  decimals, although XOF and JPY have no minor unit and TND has three.
- An amount finer than the minor unit cannot be paid. Rejecting it at construction keeps every
  `Money` settleable, and keeps `Add` and `Subtract` within the scale.
- The rounding mode of a product is a business decision: an invoice, a tax and an interest
  computation round differently. A default would make that decision silently.
- Splitting `EUR 0.10` three ways by multiplication loses a cent. Allocation by integer ratios
  is exact, and giving the remainder to the first shares is deterministic.
- Codes whose minor unit is `N.A.` in List One (precious metals, SDR, test and no-currency
  codes) cannot carry a minor-unit scale, so they are not in the table.

**JSON (#167).** `MicroKit.Domain` makes its own value objects round-trip with default
`System.Text.Json` options, through `[JsonConverter]` attributes and internal converters:
`Currency` as its code, read back through `Currency.FromCode`; `Actor` as its value, read back
through the public constructor; `Money` as `{ "Amount": …, "Currency": "EUR" }`, with names
following `PropertyNamingPolicy` and `IsZero` not serialized. Without them, a domain event carrying
a `Money` could be written to the outbox and never read back. `System.Text.Json` is part of the
shared framework, so no package is added.

**JSON and the built-in actors (#170, superseding the `Actor` part of #167).** The default path is
the safe one. JSON reaching `Actor` through its type-level converter is untrusted, so a value with
the reserved `$` prefix fails as a `JsonException` carrying the JSON path, and a built-in actor does
not round-trip with default options. Rehydrating a built-in actor is an explicit opt-in for
storage the application wrote itself: the public `TrustedActorJsonConverter` reads through
`Actor.Restore`, and is registered only in `JsonSerializerOptions.Converters`, which takes
precedence over a type-level `[JsonConverter]`. Until the Messaging outbox can register it (#174),
an event that must record a built-in actor carries the actor's value as a string.

What the default converter writes, it can read (#175). It refuses to write a built-in actor, as a
value or as a dictionary key, with a `JsonException` naming `TrustedActorJsonConverter`, so a value
that cannot cross the untrusted path fails when it is serialized, inside the business transaction,
never when an outbox processor reads it back as a poison message. `TrustedActorJsonConverter`
keeps writing every actor.

#167 first read every `Actor` through `Actor.Restore`. That made a request body bound by ASP.NET
Core able to name `$system` and be recorded as the application itself, with only an XML remark
standing in the way.

**Rejected alternatives.**
- *A separate `MicroKit.Domain.SystemTextJson` package.* A new publishable id for three
  converters (#167).
- *Keep `Actor.Restore` behind the type-level converter, guarded by documentation.* Every JSON
  payload, a request body included, could then name a built-in actor (#170).
- *Register the converters in the Messaging serializer.* A cross-module edge for a Domain defect
  (#167).
- *Derive the minor unit from `RegionInfo` or `NumberFormatInfo`.* The result depends on the
  host's ICU data and is unavailable under invariant globalization.
- *A default rounding mode, banker's or away-from-zero.* Hides the business decision.
- *Allocation by decimal percentages.* Percentages that do not sum to exactly one reintroduce
  the loss.

### D8 — A specification is written once

**Decision.** `Specification<T>.IsSatisfiedBy` is no longer abstract and cannot be overridden.
It evaluates `ToExpression()`, compiled once per instance and cached; a race may compile twice,
never yield a wrong result. `ToExpression` stays abstract. The composite specifications lose
their `IsSatisfiedBy` overrides. `ISpecification<T>` is unchanged.

**Reasoning.**
- With both members abstract, every specification wrote its predicate twice.
  `MicroKit.Persistence.Testing.InMemoryStore` evaluates `IsSatisfiedBy` while
  `EfSpecificationEvaluator` translates `ToExpression`, so in-memory tests checked the copy
  production does not run.
- Deriving the delegate from the expression leaves one source of truth.
- The cache is a plain field: two threads compiling the same expression produce equivalent
  delegates, so the race is benign and needs no lock.
- Under NativeAOT, `Expression.Compile()` falls back to the expression interpreter.

**Consequences.** `ToExpression` must return an equivalent expression on every call, since the
first compilation is kept for the lifetime of the instance. An expression only a query provider
can evaluate, such as one calling `EF.Functions`, can no longer be evaluated in memory: the
in-memory store now runs what production runs, and fails where production would need the
database. Each new instance pays one compilation on its first `IsSatisfiedBy`.

**Rejected alternatives.**
- *Keep both abstract (status quo).* Two copies that can drift.
- *Derive the expression from the delegate.* A delegate cannot be translated to SQL.
- *Compile on every call.* Pays a compilation per candidate.
- *`Lazy<T>` with thread safety.* A lock and an allocation for a benign race.
- *Change `ISpecification<T>`.* Breaks the Persistence evaluators for no gain.

### D9 — A business rule has a stable code

**Decision.** `IBusinessRule` gains `string Code { get; }`, and `BusinessRule` declares it
`abstract`, with no default. `BusinessRuleViolationException` exposes `Code`. Invariant
violations stay exceptions; `MicroKit.Domain` gains no dependency on `MicroKit.Result`.

**Reasoning.**
- A rule had only a free-text `Message`, and the exception identified it by CLR type name. A
  caller mapping violations to API error codes had to parse a message or depend on a type name.
- A stable code is a contract: consumers persist it, translate it and map it to responses.

**Rejected alternatives.**
- *A default code derived from the type name.* Renaming a class would silently change a
  contract.
- *A nullable or optional code.* Consumers could not rely on it.
- *Return `Result` from rule checks.* `MicroKit.Domain` depends on no other module in the
  monorepo graph, and a broken invariant is not an expected outcome.

## Consequences

Every decision above is a break on `preview.*`, listed in `CHANGELOG.md` under `Unreleased`.
Consumers migrate as follows:

- Persistence contracts come from `MicroKit.Persistence.Abstractions`.
- Identifier types drop their `object IEntityId.Value` implementation.
- Auditable types pass a creation instant or a `TimeProvider`, and an `Actor`; mutations call
  `Touch`.
- Concrete events declare `OccurredAt` positionally and forward it to `DomainEvent`.
- Specifications implement `ToExpression` only.
- Business rules implement `Code`.
- `Money` is built from a `Currency`; products state a rounding mode.

## References

- Issue #160 and sub-issues #111 to #119, #121, #153
- ADR-GLOBAL-002 D10 — breaks before `2.0.0`
- Reference implementation for #111 to #119: `michaelatsey/mini-ged@00a6689`,
  `libraries/MicroKit.Domain/`
- ISO 4217 List One, published by SIX Financial Information on behalf of the maintenance agency
