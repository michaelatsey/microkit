# microkit-messaging-dependencies

## Dependency Graph (as the `.csproj` files declare it)

```
MicroKit.Messaging.Abstractions
    ← MicroKit.Result                                 cross-module, CIReleaseBuild pattern
    ← MicroKit.Domain                                 cross-module, CIReleaseBuild pattern
                                                      (ADR-MSG-010: IIntegrationEvent : IEvent)
    (no PackageReference)

MicroKit.Messaging (Core)
    ← MicroKit.Messaging.Abstractions                 same module, unconditional
    ← MicroKit.Execution.Abstractions                 cross-module, CIReleaseBuild pattern (ADR-EXEC-001)
    ← Microsoft.Extensions.DependencyInjection.Abstractions
    ← Microsoft.Extensions.Hosting.Abstractions
    ← Microsoft.Extensions.Logging.Abstractions
    ← Microsoft.Extensions.Options                    (ConfigureJson is backed by IOptions — #186)
    ← Microsoft.Extensions.Options.ConfigurationExtensions
                                                      (MessagingOptions binds "MicroKit:Messaging" — #187;
                                                       EnableConfigurationBindingGenerator=true)

MicroKit.Messaging.EntityFrameworkCore
    ← MicroKit.Messaging (Core)                       same module, unconditional
    ← MicroKit.Persistence.EntityFrameworkCore        cross-module, CIReleaseBuild pattern
    ← Microsoft.EntityFrameworkCore.Relational        ToTable/HasIndex + ExecuteUpdateAsync/DeleteAsync SQL generation
    ← Microsoft.Extensions.DependencyInjection.Abstractions

MicroKit.Messaging.MediatR                            the ADR-MSG-009 carve-out
    ← MicroKit.Messaging (Core)                       same module, unconditional
    ← MicroKit.MediatR                                cross-module, CIReleaseBuild pattern
    ← MicroKit.Domain                                 cross-module, CIReleaseBuild pattern
    ← Microsoft.Extensions.DependencyInjection.Abstractions
    ← Microsoft.Extensions.Logging.Abstractions
    (MediatR and MicroKit.MediatR.Abstractions arrive transitively through MicroKit.MediatR;
     there is no direct PackageReference to MediatR or MediatR.Contracts)
```

`src/` holds exactly these four projects. No broker provider and no `MicroKit.Messaging.Testing`
project exists; the in-memory transport ships in Core, and no broker transport ships.

The glue is built on the public SPI only: `MicroKit.Messaging` grants it no `InternalsVisibleTo`.

> `MicroKit.Messaging.Abstractions` → `MicroKit.Domain` disagrees with the root graph
> (ADR-MSG-001: Messaging does not depend on Domain). ADR-MSG-010 made the edge; the root
> CLAUDE.md tracks the reconciliation in #151. `IIntegrationEvent` extends
> `MicroKit.Domain.Events.IEvent` only — not `IDomainEvent`, not `INotification`.

### Test projects

| Project | ProjectReference | Notable PackageReference |
|---|---|---|
| `MicroKit.Messaging.UnitTests` | Abstractions, Core | `Microsoft.Extensions.DependencyInjection`, `.Logging`, `.Hosting` (the generic host of the verification tests, #187), `.TimeProvider.Testing` |
| `MicroKit.Messaging.IntegrationTests` | Abstractions, Core, EntityFrameworkCore, `IntegrationTests.SecondModule` | `Microsoft.EntityFrameworkCore.Sqlite`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Testcontainers.PostgreSql`, `Microsoft.Extensions.Hosting`, `.TimeProvider.Testing` |
| `MicroKit.Messaging.IntegrationTests.SecondModule` | Abstractions | none (`IsTestProject=false`: a second assembly for the multi-store tests) |
| `MicroKit.Messaging.ArchitectureTests` | Abstractions, Core, EntityFrameworkCore, MediatR | `NetArchTest.Rules` |
| `MicroKit.Messaging.MediatR.UnitTests` | MediatR | `Microsoft.Extensions.DependencyInjection` |
| `MicroKit.Messaging.MediatR.IntegrationTests` | Abstractions, Core, EntityFrameworkCore, MediatR; cross-module: `MicroKit.MediatR`, `MicroKit.MediatR.Behaviors`, `MicroKit.Persistence.EntityFrameworkCore`, `MicroKit.Domain`, `MicroKit.Result` | `Microsoft.EntityFrameworkCore`, `.Sqlite`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Testcontainers.PostgreSql`, `Microsoft.Extensions.Hosting`, `.Logging`, `.DependencyInjection` |

Every test project also takes the test stack: `Microsoft.NET.Test.Sdk`, `xunit`,
`xunit.runner.visualstudio`, `Shouldly`, `coverlet.collector`, and `NSubstitute` (all except
ArchitectureTests and SecondModule). `MicroKit.Messaging.MediatR.IntegrationTests` compiles the
core suite's PostgreSQL fixture and Docker gate (`PostgreSqlFixture.cs`,
`DockerRequiredFactAttribute.cs`, `StoreDatabase.cs`) as linked files.

### InternalsVisibleTo

| From | To |
|---|---|
| `MicroKit.Messaging` | `MicroKit.Messaging.UnitTests`, `MicroKit.Messaging.IntegrationTests`, `MicroKit.Messaging.MediatR.IntegrationTests`, `DynamicProxyGenAssembly2` (NSubstitute) |
| `MicroKit.Messaging.EntityFrameworkCore` | `MicroKit.Messaging.IntegrationTests` |
| `MicroKit.Messaging.MediatR` | `MicroKit.Messaging.MediatR.UnitTests` |

---

## Reference Patterns — what each project uses

- **Cross-module references from a `src/` project** use the two-ItemGroup `CIReleaseBuild`
  pattern. All four `src/` projects do, for every cross-module edge in the graph above:

```xml
<!-- DEV: source ProjectReferences — CI/Release: published NuGet packages -->
<!-- ⚠ Any new cross-module dependency must be added to BOTH ItemGroups -->
<ItemGroup Condition="'$(CIReleaseBuild)' != 'true'">
  <ProjectReference Include="../../../MicroKit.Result/src/MicroKit.Result/MicroKit.Result.csproj" />
  <ProjectReference Include="../../../MicroKit.Domain/src/MicroKit.Domain/MicroKit.Domain.csproj" />
</ItemGroup>
<ItemGroup Condition="'$(CIReleaseBuild)' == 'true'">
  <PackageReference Include="MicroKit.Result" />
  <PackageReference Include="MicroKit.Domain" />
</ItemGroup>
```

- **References inside the module** (Core → Abstractions, EntityFrameworkCore → Core,
  MediatR → Core) are unconditional `ProjectReference` (ADR-GLOBAL-002 D5).
- **Test projects** (`IsPackable=false`) use unconditional `ProjectReference` for everything,
  cross-module included (`MicroKit.Messaging.MediatR.IntegrationTests`), and no `CIReleaseBuild`
  group.
- `MicroKit.Messaging.slnx` lists, under `/deps/`, every project of another module reached through
  a `ProjectReference`.

See the monorepo root `.claude/rules/cross-module-references.md` for the full canonical pattern.

---

## Allowed Package Matrix

| Project | Allowed references |
|---------|-----------------|
| `Abstractions` | `MicroKit.Result`, `MicroKit.Domain` (ADR-MSG-010). No package |
| `Core` | Abstractions, `MicroKit.Execution.Abstractions`, `Microsoft.Extensions.{DependencyInjection,Hosting,Logging}.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Options.ConfigurationExtensions` |
| `EntityFrameworkCore` | Core, `MicroKit.Persistence.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `MediatR` | Core, `MicroKit.MediatR`, `MicroKit.Domain`, `Microsoft.Extensions.{DependencyInjection,Logging}.Abstractions` |
| test projects | the `src/` projects they test, the test stack above, and the packages in the table above |

A new edge is a change to this matrix, approved before it is made.

---

> **Why `Microsoft.EntityFrameworkCore.Relational` and not base `Microsoft.EntityFrameworkCore`:**
> `ToTable()` and `HasIndex()` with database-specific options are relational-only APIs defined in
> `Microsoft.EntityFrameworkCore.Relational`. `ExecuteUpdateAsync` and `ExecuteDeleteAsync` generate
> SQL — their implementation lives in the relational assembly. The EFCore package only targets
> relational databases; the in-memory provider is never a target, so depending on `.Relational`
> directly is the correct choice.
>
> **Tested providers:** PostgreSQL (through Testcontainers) and SQLite. SQL Server is not tested.

---

## Forbidden Dependencies (non-negotiable)

> **ADR-MSG-009 carve-out:** `MicroKit.Messaging.MediatR` is the SINGLE package that reaches
> `MediatR` / `MediatR.Contracts`, through `MicroKit.MediatR`. Its two test projects reach it too,
> and `MicroKit.Messaging.ArchitectureTests` receives it transitively through its reference to the
> glue, which it inspects. The architecture tests pin the rest: no MediatR in Core
> (`Core_HasNoMediatRDependency`), no MediatR.Contracts in Abstractions, Core or EntityFrameworkCore
> (`AllAssemblies_HaveNoMediatRContractsDependency`), no MediatR in EntityFrameworkCore
> (`EfCore_HasNoMediatRDependency`).

```
❌ MediatR / MediatR.Contracts — directly or through MicroKit.MediatR, in any project EXCEPT
            MicroKit.Messaging.MediatR and its test projects (ADR-MSG-009)
            (messaging has its own IIntegrationEvent, not INotification)
❌ MicroKit.Auth — in any project
❌ MicroKit.Tenancy — in any project (ADR-EXEC-001: the host composes a tenant-aware
            IExecutionScopeFactory)
❌ MicroKit.Http — in any project
❌ Microsoft.EntityFrameworkCore (any package) — in Abstractions, Core or MediatR
❌ RabbitMQ.Client — in Core or Abstractions
❌ Azure.Messaging.ServiceBus — in Core or Abstractions
❌ Confluent.Kafka — in Core or Abstractions
❌ MicroKit.Persistence.EntityFrameworkCore — in any src/ project other than
            MicroKit.Messaging.EntityFrameworkCore (MicroKit.Messaging.MediatR.IntegrationTests
            references it, test-only)
❌ Circular dependency between any two projects
❌ FluentAssertions — in any project; commercial licence (Xceed EULA v8+). Shouldly is the
            assertion library
```

---

## Directory.Packages.props

All version pins live in the monorepo root `Directory.Packages.props`
(`CentralPackageTransitivePinningEnabled=true`).
Never add `Version=` attribute directly in `.csproj` files.
Any new package requires an entry there first.
