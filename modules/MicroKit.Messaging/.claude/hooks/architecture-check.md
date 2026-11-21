# Hook: architecture-check — MicroKit.Messaging

Runs on demand or as part of the CI pipeline to verify architectural invariants.
Invoke manually via: `dotnet test modules/MicroKit.Messaging/tests/MicroKit.Messaging.ArchitectureTests/`

## Checks

### 1. IOutboxWriter / IOutboxProcessorStore not in Persistence

```bash
grep -rn "IOutboxWriter\|IOutboxProcessorStore\|IInboxGuardStore\|IInboxProcessorStore" \
  modules/MicroKit.Persistence/ --include="*.cs" --include="*.csproj"
```

Fail if any match found. The outbox and inbox store contracts must live in
`MicroKit.Messaging.Abstractions` (namespace `MicroKit.Messaging.Spi`) — never in
`MicroKit.Persistence.Abstractions`. `IInboxWriter` and `IInboxSettlementStore` were removed by
#188 (ADR-MSG-020); the grep names the contracts that exist, so the check keeps guarding something
rather than passing forever on a type nobody can write.

### 2. IIntegrationEvent used (not INotification)

```bash
grep -rn "INotification\|MediatR\.INotification" \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/ \
  modules/MicroKit.Messaging/src/MicroKit.Messaging/ \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.EntityFrameworkCore/ \
  --include="*.cs" | grep -v "^[^:]*:[0-9]*:\s*///"
```

Fail if any match found. All integration events implement `IIntegrationEvent` from this module.
`MicroKit.Messaging.MediatR` and its test projects are excluded: they deliver MediatR
notifications by design (ADR-MSG-009). XML-doc lines are excluded: `IIntegrationEvent`'s docs
name `INotification` to say it does not extend it.

### 3. No broker coupling in Abstractions or Core

```bash
grep -rn "RabbitMQ\|Azure\.Messaging\|Confluent\.Kafka\|ServiceBus\|AMQP" \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/ \
  modules/MicroKit.Messaging/src/MicroKit.Messaging/ \
  --include="*.cs" --include="*.csproj" | grep -v "^[^:]*:[0-9]*:\s*///"
```

Fail if any match found. Broker coupling is confined to provider packages only. XML-doc lines are
excluded: `IMessageTransport` and `IEnvelopeReceiver` name each broker's acknowledgement rule.
`Core_HasNoBrokerDependency` pins the assembly.

### 4. Testing package is Abstractions-only

```bash
csproj=modules/MicroKit.Messaging/src/MicroKit.Messaging.Testing/MicroKit.Messaging.Testing.csproj
[ -f "$csproj" ] && grep -n "MicroKit\.Messaging/MicroKit\.Messaging\.csproj\|Include=\"MicroKit\.Messaging\"" "$csproj"
  # (MicroKit.Messaging.Testing is planned but not built — L0 finding #19)
```

Fail if `MicroKit.Messaging` (Core) appears as a dependency of `MicroKit.Messaging.Testing` —
once that project exists. It does not today, so this check is inert.
Testing depends on Abstractions only.

> The patterns match the Core project path and the exact package name, and avoid false positives
> from path strings containing the module name.

### 5. EF Core confined to EntityFrameworkCore package

```bash
grep -rn "using Microsoft\.EntityFrameworkCore\|Include=\"Microsoft\.EntityFrameworkCore" \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/ \
  modules/MicroKit.Messaging/src/MicroKit.Messaging/ \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.MediatR/ \
  --include="*.cs" --include="*.csproj"
```

Fail if any match found. EF Core types must not appear in Abstractions, Core or the MediatR glue
(also pinned by `Abstractions_HasNoEfCoreDependency`, `Core_HasNoEfCoreDependency` and
`MediatRGlue_HasNoEfCoreDependency`). The words `DbContext` and `DbSet` are not grepped: the docs
and log messages of Core and Abstractions name the store's `DbContext`, which a store enlists in.

### 6. TenantId present on OutboxMessage and InboxMessage

```bash
grep -n "TenantId" \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/Spi/Entities/OutboxMessage.cs \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/Spi/Entities/InboxMessage.cs
```

Fail if `TenantId` is absent from either file. Tenant isolation is mandatory; the column is
`string?` — null in a single-tenant deployment (ADR-MSG-008 §5).

## Scope

This hook applies **only to files under `modules/MicroKit.Messaging/`**.
