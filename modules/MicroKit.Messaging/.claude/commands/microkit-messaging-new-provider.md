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
- Default: `System.Text.Json` via `MicroKit.Messaging.Serialization` (v2)
- `MessageEnvelope<T>` serialization must be symmetric (serialize → deserialize → same object)

### Error Handling
- Non-transient broker errors → `IOutboxStore.MarkFailedAsync`
- Transient errors → retry with back-off (defer to OutboxProcessor retry logic)
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
