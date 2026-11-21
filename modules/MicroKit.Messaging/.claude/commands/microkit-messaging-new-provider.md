---
description: Scaffold a new broker provider adapter in MicroKit.Messaging (e.g., RabbitMQ, AzureServiceBus, Kafka). Produces an implementation plan for the provider package structure, IMessageTransport adapter, DI registration, and tests.
---

Use the microkit-messaging-implementer agent.

Load in order:
1. `.claude/CLAUDE.md`
2. `.claude/rules/microkit-messaging-architecture.md`
3. `.claude/rules/microkit-messaging-dependencies.md`
4. `.claude/rules/microkit-messaging-naming.md`
5. `.claude-context/templates/microkit-messaging-provider-template/` (if present)

Produce an implementation plan for a new broker provider: $ARGUMENTS

The plan must cover:

### Package Structure
- Package name: `MicroKit.Messaging.{ProviderName}`
- Current status: `IsPackable=false` scaffold → `IsPackable=true` when complete
- Target framework: `net10.0`

### Core Components
- Transport adapter: `{ProviderName}MessageTransport : IMessageTransport`
  - ⚠ **`IMessagePublisher` no longer exists** — it was deleted by ADR-MSG-018 and its in-process
    implementation by ADR-MSG-019. A provider implements `IMessageTransport.SendAsync(MessageEnvelope, ct)`
    on the sending side. It does **not** implement `IOutboxDispatcher`, which is internal to Core
    (ADR-MSG-020): `TransportOutboxDispatcher` (Core) serves every outbox row with a `null`
    `Endpoint` and feeds your transport.
  - **No broker `IMessageTransport` ships in any MicroKit package.** The only implementation is
    Core's internal in-memory transport (`OnlyCoreShipsAnIMessageTransport_AndItIsTheInternalInMemoryOne`),
    so yours is the first broker one — which is why the acknowledgement rule below is a
    conformance obligation rather than a convention it could be checked against.
- Consume loop: hands each arriving broker message, decoded into a `MessageEnvelope`, to
  `IEnvelopeReceiver.ReceiveAsync` (implemented in Core) and settles the broker message from the
  returned `EnvelopeReceiveResult.Disposition` (`Acknowledge`, `DeadLetter`, `Redeliver`,
  `PauseAndRedeliver`, `Unsettled` — never acknowledge on `Unsettled`), only after the returned
  task completes. See README *Receiving messages*.
- Connection/channel management: `{ProviderName}ConnectionManager` (if stateful broker)
- Options: `{ProviderName}MessagingOptions` (sealed record)
- DI extension: `Add{ProviderName}Transport()` on `MessagingBuilder` (not `Add{ProviderName}Messaging()` on `IServiceCollection`)

### Serialization
- A provider implements **`IMessageTransport`**, not `IOutboxDispatcher`. It receives a
  `MessageEnvelope` — already built from the outbox row by `TransportOutboxDispatcher` — whose
  `Body` is **opaque UTF-8 JSON**. A provider does not deserialize it and needs no
  `IMessageSerializer`: the receiving process resolves the envelope's `Type` (the wire type) to its
  own local type.
- **`SendAsync` must not return before the broker has acknowledged the message.** The processor
  marks the row `Published` on that return and `Published` is terminal, so an asynchronous hand-off
  makes the mark a lie. **Your provider owes a conformance test proving this**; one that fails it is
  unusable whether or not it compiles.
- **Register the `IMessageTransport`, and nothing else on the send side.** Every store registers
  the (internal) `TransportOutboxDispatcher`, which sends a store's transport rows through whatever
  `IMessageTransport` the host has; `IOutboxDispatcher` and `AddTransportDispatcher()` are internal
  and a provider never names them. Register one transport: a second is refused by
  `UseInMemoryTransport()` and shadows the first otherwise.
- A source-generated implementation is planned for `MicroKit.Messaging.Serialization` (v2)

### Error Handling — signal permanence with typed exceptions, never by writing state
The provider does **not** settle outbox rows. `OutboxProcessor` owns every state transition; the
transport's only job is to throw the right type, which `TransportOutboxDispatcher` lets through.

| Throw | When | Processor response |
|---|---|---|
| `OutboxPayloadException` | the message can never be sent without changing — a body or a type the broker rejects outright | dead-letter on FIRST sight |
| `OutboxTransportUnavailableException` | connection refused, broker down, auth rejected, channel closed — the next message is certain to fail too | the store's transport rows deferred until the transport's back-off ends, **no retry consumed**; its local rows are still delivered |
| anything untyped | one message rejected while the transport is healthy | transient — retry with back-off |

- ⚠ **Never throw `OutboxPayloadException` for** a broker nack, a timeout, a refused connection,
  an HTTP 503 or a database timeout. Only proven permanence dead-letters; a provider that guesses
  wrongly in this direction loses messages.
- `IOutboxStore.MarkFailedAsync` **does not exist** — that API was deleted by the outbox claim
  rewrite. A provider that tries to write status is reaching outside its seam.
- Never silently swallow exceptions

### Tests
- Unit: `SendAsync` happy path + failure classification (broker unavailable →
  `OutboxTransportUnavailableException`, never `OutboxPayloadException`)
- **Conformance: `SendAsync` does not return before the broker acknowledges.** Non-negotiable —
  see the Serialization section. This is the test that makes the provider usable.
- Integration: publish → consume round-trip (using TestContainers or emulator)

### Dependencies
- No other broker provider packages referenced
- `MediatR.Contracts` remains forbidden
- All new NuGet versions added to root `Directory.Packages.props`

### RabbitMQ-specific notes (if provider = RabbitMQ)
- Client: `RabbitMQ.Client` v7 (new async API — not v6 sync API)
- Connection: `IConnection` obtained via `IConnectionFactory.CreateConnectionAsync()`
- Channel: `IChannel` (not `IModel`) — v7 renamed the channel interface
- Publish: `IChannel.BasicPublishAsync()` (async, not `BasicPublish()`)
- Declare exchanges/queues idempotently on startup

Wait for explicit approval before writing any code.
Do not commit anything.
