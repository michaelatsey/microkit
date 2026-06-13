# Hook: architecture-check — MicroKit.Messaging

Runs on demand or as part of the CI pipeline to verify architectural invariants.
Invoke manually via: `dotnet test modules/MicroKit.Messaging/tests/MicroKit.Messaging.ArchitectureTests/`

## Checks

### 1. IOutboxWriter / IOutboxProcessorStore not in Persistence

```bash
grep -rn "IOutboxWriter\|IOutboxProcessorStore\|IInboxStore" \
  modules/MicroKit.Persistence/ --include="*.cs" --include="*.csproj"
```

Fail if any match found. `IOutboxWriter`, `IOutboxProcessorStore`, and `IInboxStore` must live in
`MicroKit.Messaging.Abstractions` — never in `MicroKit.Persistence.Abstractions`.

### 2. IIntegrationEvent used (not INotification)

```bash
grep -rn "INotification\|MediatR\.INotification" \
  modules/MicroKit.Messaging/ --include="*.cs"
```

Fail if any match found. All integration events implement `IIntegrationEvent` from this module.

### 3. No broker coupling in Abstractions or Core

```bash
grep -rn "RabbitMQ\|Azure\.Messaging\|Confluent\.Kafka\|ServiceBus\|AMQP" \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/ \
  modules/MicroKit.Messaging/src/MicroKit.Messaging/ \
  --include="*.cs" --include="*.csproj"
```

Fail if any match found. Broker coupling is confined to provider packages only.

### 4. Testing package is Abstractions-only

```bash
grep -n "Include=\"MicroKit\.Messaging\"" \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.Testing/MicroKit.Messaging.Testing.csproj
```

Fail if `MicroKit.Messaging` (Core) appears as a dependency of `MicroKit.Messaging.Testing`.
Testing depends on Abstractions only.

> The pattern `Include="MicroKit\.Messaging"` (with surrounding quotes) matches the exact
> package name and avoids false positives from path strings containing the module name.

### 5. EF Core confined to EntityFrameworkCore package

```bash
grep -rn "EntityFrameworkCore\|DbContext\|DbSet" \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/ \
  modules/MicroKit.Messaging/src/MicroKit.Messaging/ \
  --include="*.cs" --include="*.csproj"
```

Fail if any match found. EF Core types must not appear in Abstractions or Core.

### 6. TenantId present on OutboxMessage and InboxMessage

```bash
grep -n "TenantId" \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/OutboxMessage.cs \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/InboxMessage.cs
```

Fail if `TenantId` is absent from either file. Tenant isolation is mandatory.

## Scope

This hook applies **only to files under `modules/MicroKit.Messaging/`**.
