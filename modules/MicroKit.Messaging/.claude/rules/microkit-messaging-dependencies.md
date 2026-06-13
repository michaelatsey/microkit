# microkit-messaging-dependencies

## Dependency Graph (authoritative)

```
MicroKit.Messaging.Abstractions
    ← MicroKit.Result
    ← MicroKit.Domain.Abstractions

MicroKit.Messaging (Core)
    ← MicroKit.Messaging.Abstractions
    ← Microsoft.Extensions.DependencyInjection.Abstractions
    ← Microsoft.Extensions.Hosting.Abstractions
    ← Microsoft.Extensions.Logging.Abstractions

MicroKit.Messaging.EntityFrameworkCore
    ← MicroKit.Messaging (Core)
    ← MicroKit.Persistence.EntityFrameworkCore       ← cross-module (Level 2)
    ← Microsoft.EntityFrameworkCore

MicroKit.Messaging.Testing
    ← MicroKit.Messaging.Abstractions                ← Abstractions only, never Core

── v2 providers (IsPackable=false) ─────────────────────────────────────────────
MicroKit.Messaging.RabbitMQ             [Phase 2]
    ← MicroKit.Messaging (Core)
    ← RabbitMQ.Client (v7+)

MicroKit.Messaging.AzureServiceBus      [Phase 2]
    ← MicroKit.Messaging (Core)
    ← Azure.Messaging.ServiceBus

MicroKit.Messaging.Kafka                [Phase 2]
    ← MicroKit.Messaging (Core)
    ← Confluent.Kafka

MicroKit.Messaging.OpenTelemetry        [Phase 2]
    ← MicroKit.Messaging (Core)
    ← OpenTelemetry.Api

MicroKit.Messaging.Serialization        [Phase 2]
    ← MicroKit.Messaging.Abstractions
    ← System.Text.Json (BCL — no extra package needed for net10.0)
```

---

## Cross-Module References — Mandatory Pattern

Any cross-module dependency MUST use the two-ItemGroup CIReleaseBuild pattern:

```xml
<!-- DEV: source ProjectReferences — CI/Release: published NuGet packages -->
<!-- ⚠ Any new cross-module dependency must be added to BOTH ItemGroups -->
<ItemGroup Condition="'$(CIReleaseBuild)' != 'true'">
  <ProjectReference Include="../../../../modules/MicroKit.Result/src/MicroKit.Result/MicroKit.Result.csproj" />
  <ProjectReference Include="../../../../modules/MicroKit.Domain/src/MicroKit.Domain.Abstractions/MicroKit.Domain.Abstractions.csproj" />
</ItemGroup>
<ItemGroup Condition="'$(CIReleaseBuild)' == 'true'">
  <PackageReference Include="MicroKit.Result" />
  <PackageReference Include="MicroKit.Domain.Abstractions" />
</ItemGroup>
```

See monorepo root `.claude/rules/cross-module-references.md` for the full canonical pattern.

---

## Allowed Package Matrix

| Project | Allowed packages |
|---------|-----------------|
| `Abstractions` | `MicroKit.Result`, `MicroKit.Domain.Abstractions` |
| `Core` | Abstractions + `Microsoft.Extensions.{DI,Hosting,Logging}.Abstractions` |
| `EntityFrameworkCore` | Core + `MicroKit.Persistence.EntityFrameworkCore` + `Microsoft.EntityFrameworkCore` |
| `Testing` | Abstractions only — `xunit`, `Shouldly`, `NSubstitute` belong in the consumer's test project, NOT in this library |
| `RabbitMQ` | Core + `RabbitMQ.Client` |
| `AzureServiceBus` | Core + `Azure.Messaging.ServiceBus` |
| `Kafka` | Core + `Confluent.Kafka` |
| `OpenTelemetry` | Core + `OpenTelemetry.Api` |
| `Serialization` | Abstractions (System.Text.Json is BCL in net10.0) |

---

## Forbidden Dependencies (non-negotiable)

```
❌ MediatR.Contracts — in ANY package (Abstractions, Core, EFCore, Testing, providers, tests)
❌ MediatR — in ANY package (messaging has its own IIntegrationEvent, not INotification)
❌ MicroKit.Auth — in any package
❌ MicroKit.Multitenancy — in any package
❌ MicroKit.Http — in any package
❌ Microsoft.EntityFrameworkCore — in Abstractions or Core
❌ RabbitMQ.Client — in Core or Abstractions
❌ Azure.Messaging.ServiceBus — in Core or Abstractions
❌ Confluent.Kafka — in Core or Abstractions
❌ MicroKit.Persistence.EntityFrameworkCore — outside of .EntityFrameworkCore package
❌ Circular dependency between any two packages
❌ FluentAssertions — commercial licence (Xceed EULA v8+)
```

---

## Test Projects — Unconditional References

Test projects (`IsPackable=false`) use unconditional `ProjectReference`:

```xml
<!-- ✅ Test projects — always unconditional, never packed -->
<ProjectReference Include="../../src/MicroKit.Messaging.Abstractions/MicroKit.Messaging.Abstractions.csproj" />
<ProjectReference Include="../../src/MicroKit.Messaging/MicroKit.Messaging.csproj" />
```

---

## Directory.Packages.props

All version pins live in the monorepo root `Directory.Packages.props`.
Never add `Version=` attribute directly in `.csproj` files.
Any new package requires an entry there first.
