---
description: Scaffold a new broker provider adapter in MicroKit.Messaging (e.g., RabbitMQ, AzureServiceBus, Kafka). Produces an implementation plan for the provider package structure, IMessagePublisher adapter, DI registration, and tests.
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
- Publisher adapter: `{ProviderName}MessagePublisher : IMessagePublisher`
- Connection/channel management: `{ProviderName}ConnectionManager` (if stateful broker)
- Options: `{ProviderName}MessagingOptions` (sealed record)
- DI extension: `Add{ProviderName}Transport()` on `MessagingBuilder` (not `Add{ProviderName}Messaging()` on `IServiceCollection`)

### Serialization
- A provider implements **`IMessageTransport`**, not `IOutboxDispatcher`. It receives a
  `MessageEnvelope` — already built from the outbox row by `TransportOutboxDispatcher` — whose
  `Payload` is **opaque JSON**. A provider does not deserialize it and needs no
  `IMessageSerializer`: the receiving process resolves `ContractName` to its own local type.
- **`SendAsync` must not return before the broker has acknowledged the message.** The processor
  marks the row `Published` on that return and `Published` is terminal, so an asynchronous hand-off
  makes the mark a lie. **Your provider owes a conformance test proving this**; one that fails it is
  unusable whether or not it compiles.
- Call `AddTransportDispatcher()` from your `Add{ProviderName}Transport()` so a consumer writes one
  line rather than two.
- A source-generated implementation is planned for `MicroKit.Messaging.Serialization` (v2)

### Error Handling — signal permanence with typed exceptions, never by writing state
The provider does **not** settle messages. `OutboxProcessor` owns every state transition; a
dispatcher's only job is to throw the right type.

| Throw | When | Processor response |
|---|---|---|
| `OutboxPayloadException` | the row can never be dispatched without changing — unresolvable `EventType`, malformed JSON, a contract the broker rejects outright | dead-letter on FIRST sight |
| `OutboxTransportUnavailableException` | connection refused, broker down, auth rejected, channel closed — the next message is certain to fail too | abort batch, release remainder, **no retry consumed** |
| anything untyped | one message rejected while the transport is healthy | transient — retry with back-off |

- ⚠ **Never throw `OutboxPayloadException` for** a broker nack, a timeout, a refused connection,
  an HTTP 503 or a database timeout. Only proven permanence dead-letters; a provider that guesses
  wrongly in this direction loses messages.
- `IOutboxStore.MarkFailedAsync` **does not exist** — that API was deleted by the outbox claim
  rewrite. A provider that tries to write status is reaching outside its seam.
- Never silently swallow exceptions

### Tests
- Unit: publisher happy path + failure (broker unavailable)
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
