# ADR-004: Amendments to the Settled Domain Surface

## Status
**Accepted** — implemented by issues #181 and #182, before `2.0.0`. Supersedes in part
ADR-003 D7: the paragraph on the #175 write refusal.

## Context

ADR-003 settled the `MicroKit.Domain` surface before `2.0.0`. Two questions came back after it
merged:

- #175 made the default `System.Text.Json` converter of `Actor` refuse to write a built-in actor,
  so that a value it could not read back would fail inside the business transaction. #180 showed
  the cost: every serialization with default options failed on a built-in actor, an HTTP response
  or a log as much as a store. MediatR's idempotency behaviour threw after the command had
  committed, so a retry ran it again, and its caching behaviour failed every query whose response
  carried a built-in actor (#181).
- PR #169 left two questions under "Needs decision": the allocation of `Mutate`, and the domain
  events a plain `AggregateRoot<TId>` keeps after a domain method throws (#182).

This ADR records those decisions, with the alternatives rejected.

## Decisions

### D1 — The default `Actor` converter is asymmetric (#181)

**Decision.** The type-level converter of `Actor` writes every actor, built-in ones included, as
its `Value`, both as a value and as a dictionary key. It reads untrusted input only: a value with
the reserved `$` prefix fails as a `JsonException` carrying the JSON path (#170, unchanged).
`TrustedActorJsonConverter` is unchanged: it writes as the default converter does, and reads
through `Actor.Restore`. A built-in actor written with default options cannot be read back
without `TrustedActorJsonConverter` registered.

The outbox risk the write refusal covered is left to the existing rule: until the Messaging
outbox can register `TrustedActorJsonConverter` (#174), an event that must record a built-in
actor carries the actor's `Value` as a `string`, and the consumer rebuilds it with
`Actor.Restore`.

This supersedes the #175 paragraph of ADR-003 D7 ("What the default converter writes, it can
read"). The rest of D7, the #170 read guard included, stands.

**Reasoning.**
- Writing a built-in actor carries no risk. Only reading untrusted JSON does, and #170 already
  guards that path.
- The write refusal traded a contained failure, one event that an outbox processor cannot read,
  for a wider one: every serialization with default options, including the paths that never read
  back. #180 is the worst case, a side effect committed and reported as failed.
- A response or a log line is written by the application and never read back by it. Storage the
  application reads back itself (an outbox, an event store, a cache, an idempotency store) knows
  its own options, and registers `TrustedActorJsonConverter` on the options it reads with.

**Rejected alternatives.**
- *Keep the write refusal (#175, status quo).* Breaks every write-only path, as above.
- *Write a built-in actor as `null` or omit it.* Loses the audit value silently.
- *Write a built-in actor without its prefix.* `system` would read back as a user named `system`:
  the collision the reserved prefix exists to prevent (ADR-003 D5).
- *Read the reserved values through `Actor.Restore` by default.* A request body could name
  `$system` and be recorded as the application itself (#170).
- *Register `TrustedActorJsonConverter` from MicroKit.MediatR or MicroKit.Messaging.* Out of
  scope for a Domain decision, and #174 owns the Messaging side.

### D2 — `Mutate` keeps a single overload (#182)

**Decision.** `AuditableEntity<TId>` and `AuditableAggregateRoot<TId>` keep the single
`Mutate(DateTimeOffset at, Actor by, Action mutation)`. Its XML remarks state the cost: a lambda
that captures a parameter or a local allocates a closure and a delegate on each call.

**Reasoning.**
- `Mutate` wraps a business mutation, which is followed by the I/O that persists it. One closure
  and one delegate are negligible next to that I/O.
- `Mutate<TState>(DateTimeOffset, Actor, TState, Action<TState>)` is an additive overload: it can
  ship after `2.0.0` without a break, if a benchmark ever shows a cost.

**Rejected alternatives.**
- *Add `Mutate<TState>` now.* Surface without a measured need, and a second way to write every
  mutation.
- *Replace `Action` with `Action<TState>`.* A break, and noisier call sites for every consumer.

### D3 — Event rollback stays internal to the auditable path (#182)

**Decision.** Only `AuditableAggregateRoot<TId>.Mutate` removes the domain events of a mutation
that throws, through the internal `IEventJournal`. A plain `AggregateRoot<TId>` keeps the events
raised by a method that throws, and its state changes. The limit is documented in the XML
remarks of `AggregateRoot<TId>` (class and `RaiseDomainEvent`), of both `Mutate` methods, and in
`docs/guides/Implementing-Aggregates.md`: after a domain method throws, the aggregate instance
must be discarded, not saved.

**Reasoning.**
- A plain aggregate has no point of passage around its methods: the base class cannot tell where
  an operation starts or ends, so it cannot know which events to remove.
- Removing the events would not be enough anyway: the state changed before the throw stays, so the
  instance is unsafe to save either way. Discarding it is the rule that covers both.
- A `protected Atomically(Action)` is additive and can ship later, after `2.0.0`, without a break.

**Rejected alternatives.**
- *Expose `IEventJournal` or a public rollback.* Surface for a partial fix: state is not rolled
  back.
- *Add `Atomically(Action)` now.* Surface without a demonstrated need; additive later.
- *Snapshot and restore the whole aggregate.* Needs a copy of arbitrary derived state, which the
  base class cannot make.

### Decisions taken during the review of this branch

The rule applied: keep the published surface stable, preferring an additive change or a documented
limit over a break; break only to remove an incorrect or unsafe behaviour; follow ADR-003, this ADR
and the official .NET and EF Core guidance.

#### D4 — `Actor.Restore` trims before matching the built-in actors

**Decision.** `Actor.Restore` removes leading and trailing white space before it compares the value
with the built-in identifiers, as the public constructor does before validating. `" $system "`
restores `Actor.System`; `" alice "` restores `alice`, as before.

**Reasoning.** The constructor trimmed a user value, so a padded user value already restored, while
a padded built-in value fell through to the constructor and was rejected for its `$` prefix. A
fixed-width column (`char`, `nchar`) pads what it stores, so a value converter reading one back
failed on every built-in actor. The change is additive: it only accepts inputs that threw.

**Rejected alternatives.**
- *Document that the value must be passed unpadded.* Leaves the two arms of the same method
  normalising differently.
- *Trim in the value converter.* Every consumer would have to know it.

#### D5 — `Email` accepts only the characters of a bare address

**Decision.** After trimming, `Email` rejects an address that contains white space or a control
character, a line break included, with a `DomainException`. The local part accepts letters,
digits and combining marks of any script (RFC 6531), read code point by code point, and the
RFC 5322 `atext` symbols plus the dot; the domain accepts letters, digits, combining marks, `-` and
the dot. Variation selectors and U+034F, invisible marks, are excluded. Any other character is
rejected. The validation stays lexical: domain label lengths and hyphens, and look-alike letters
from different scripts, are documented as not checked.

**Reasoning.** The validation checked the `@`, the dots and the lengths only, so
`"victim@example.com\r\nBcc: attacker@example.org"` was a valid `Email`. An application that puts an
address in a mail header could then send to a recipient the caller chose: header injection, an
unsafe behaviour, which the rule allows a break to remove. No unquoted address contains white space
or a control character (RFC 5322 §3.4.1); a quoted local part may, but the type never parsed quoting,
so it accepted those by accident, not by design. For the same reason a value could hold a comma
or a semicolon, which a header reads as a second recipient, an angle bracket, or an invisible format
character such as U+200B or U+202E, which makes two different values look identical. An allow-list
closes the class of defect, where a deny-list of white space only closed one instance of it.

**Rejected alternatives.**
- *Strip the characters.* Stores an address other than the one supplied.
- *Full RFC 5322 parsing, quoted local parts included.* A new validator for addresses that mail
  providers do not issue; out of proportion for a lightweight type.
- *`System.Net.Mail.MailAddress`.* It parses a display name and an address, not a bare address, so
  it accepts input this type must reject.

#### D6 — `PhoneNumber` validates what it keeps

**Decision.** `PhoneNumber` keeps the ASCII digits, a leading `+` and one extension marker `x`
(an `X` is stored as `x`). Spaces and `- . ( ) /` are formatting and are removed; any other
character is rejected. A `+` anywhere but first, a second marker or an empty extension is rejected.
The 7 to 15 digits of ITU-T E.164 are counted before the extension only; the extension has 1 to
10 digits.

**Reasoning.** The type dropped every character it did not keep, so
`"call me at 0612345678 tomorrow"` was a valid number, and `char.IsDigit` let non-ASCII digits
through, which no dialler or E.164 consumer accepts. Its own message said a `+` was allowed only at
the beginning, but the check ran only when the number started with one. `"1234567x8x9"` passed with
two markers. Extension digits counted toward the E.164 maximum, which applies to the number alone,
so a valid number with an extension could be rejected. `X12` and `x12` were stored differently and
compared unequal. Each is an incorrect behaviour, which the rule allows a break to remove. Counting
the extension apart from the E.164 digits removed the only bound on it, so the extension gets its
own: E.164 does not define extensions, and 10 digits covers private branch exchanges while keeping
a value under 30 characters.

**Rejected alternatives.**
- *Recognise spelled-out markers such as `ext.`.* New parsing rules for one input shape the type
  never documented; `x` stays the one marker.
- *Keep dropping unknown characters.* It is the defect.
- *A full numbering-plan validation (libphonenumber).* A third-party dependency, which the module
  forbids.

#### D7 — `EntityId<T>.ToString` is sealed

**Decision.** `EntityId<T>.ToString()` is `sealed override`, so every derived identifier returns its
`Guid` value, as the member documents.

**Reasoning.** The C# compiler synthesizes `ToString` in each derived record unless the base
declares it `sealed`. Every identifier deriving from `EntityId<T>` therefore printed
`OrderId { Value = … }`, against the documented "string representation of the underlying Guid
value", and `EntityNotFoundException` messages carried that text. Sealing is the language's own
remedy. It breaks only a derived record that declares its own `ToString`, a break the rule allows
to remove an incorrect behaviour.

**Rejected alternatives.**
- *Document the limit and let each identifier override `ToString`.* The default stays wrong for
  every consumer who does not read it.
- *Format the identifier in `EntityNotFoundException`.* Fixes one caller out of all.

#### D8 — `Actor` rejects control and format characters

**Decision.** An actor identifier, built by the constructor or by `Actor.Restore`, rejects a control
character (Unicode category Cc, line breaks included), a format character (Cf, such as U+200B or
U+202E, or a tag character outside the BMP), a line or paragraph separator (Zl, Zp: U+2028,
U+2029) or an unpaired surrogate with a `DomainException`.

**Reasoning.** An actor is built from a token claim or a header and ends up in audit fields and in
the log lines built from them. `"alice\nINFO forged audit line"` was a valid actor, so a caller
could forge a line in an audit log, and U+2028 does the same in any viewer that renders it as a line
break; an invisible format character made two actors look identical. An unpaired surrogate is
written by `System.Text.Json` as U+FFFD, so the actor read back was not the one written.
No identity provider issues identifiers with such characters, so the break only removes an unsafe
behaviour.

**Rejected alternatives.**
- *Strip the characters.* Two different inputs would become the same actor.
- *Restrict actors to ASCII.* Identifiers such as service-account display names may be non-ASCII;
  only the invisible and control characters are unsafe.

#### D9 — `Actor.MaxLength` stays 200, as a documented limit

**Decision.** `Actor.MaxLength` stays `200`. The `Actor` remarks, which said an OIDC `sub` fits
without conversion, now state that OpenID Connect allows a `sub` of up to 255 characters and that a
longer identifier is rejected.

**Reasoning.** OpenID Connect Core 1.0 §2 caps `sub` at 255 ASCII characters, so a conformant
identifier of 201 to 255 characters was rejected while the documentation promised it would fit.
`MaxLength` is a `public const`: its value is compiled into every caller, such as a column sized
from it, so raising it would let values longer than those columns into the domain. The rule prefers
a documented limit to that break.

**Rejected alternatives.**
- *Raise `MaxLength` to 255.* Consumers that sized a column from the compiled constant would
  overflow it.
- *Turn `MaxLength` into a `static readonly` field.* A binary break for every caller, for the same
  outcome.

#### D10 — Converting a null `Email` or `PhoneNumber` to `string` gives `null`

**Decision.** The implicit conversions from `Email` and `PhoneNumber` to `string` return `null` for
a null instance, annotated `[return: NotNullIfNotNull]`, instead of dereferencing it.

**Reasoning.** `string s = (Email?)null;` threw a `NullReferenceException` from inside the
operator, at a call site that shows no member access; the .NET design guidelines rule out an
exception from an implicit conversion. Returning `null` is what the language does for any
reference conversion. The return type stays `string`, so the change is binary compatible, and the
annotation keeps a non-null argument's result non-null for the compiler.

**Rejected alternatives.**
- *Throw `ArgumentNullException`.* Still an exception from an implicit conversion.
- *Remove the conversions.* A break to fix a null case.

## Consequences

- A built-in actor serializes with any options: `Actor.System` is written `"$system"`.
- Reading it back requires `TrustedActorJsonConverter` in `JsonSerializerOptions.Converters`,
  registered only for storage the application wrote itself.
- An event that carries an `Actor` whose value is a built-in actor is written to an outbox with
  default options, and fails only when it is read back. The `string` rule above prevents it until
  #174.
- A store that reads back what it wrote with the same options, such as MicroKit.MediatR's
  idempotency store and caching behaviour, now writes a response carrying a built-in actor and fails
  when it reads it back, unless `TrustedActorJsonConverter` is registered on those options. That
  registration belongs to the store's module, as #174 does for Messaging.
- A value persisted before D5, D6 or D8 that the new rules reject (an `Email` with white space or a
  comma, a `PhoneNumber` with a letter, a second `x` or an inner `+`, an `Actor` with a control
  character) no longer rehydrates: the value converter or deserializer throws `DomainException` when
  the row is read. Clean such values in the store before deploying; a `PhoneNumber` stored with `X`
  rehydrates and reads back with `x`.
- `Mutate` allocates per call when its lambda captures; no API changes (D2).
- After a domain method throws, the aggregate instance is discarded, not saved (D3).

## References

- Issues #181, #182, #180, #175, #174, #170
- ADR-003 D5, D7
