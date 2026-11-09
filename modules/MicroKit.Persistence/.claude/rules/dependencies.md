# Rule: Dependencies — MicroKit.Persistence

## Central Package Management

Every version lives in the root `Directory.Packages.props`
(`CentralPackageTransitivePinningEnabled=true`). Never `Version=` on a `PackageReference`.

## Project and Package Matrix

| Project | Project reference | Packages |
|---------|-------------------|----------|
| `Abstractions` | — | `MicroKit.Domain` (cross-module) |
| core (`MicroKit.Persistence`) | Abstractions | `MicroKit.Result` (cross-module) |
| `EntityFrameworkCore` | core | `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `EntityFrameworkCore.PostgreSql` | EntityFrameworkCore | `Npgsql.EntityFrameworkCore.PostgreSQL` |
| `EntityFrameworkCore.SqlServer` | EntityFrameworkCore | `Microsoft.EntityFrameworkCore.SqlServer` |
| `Specifications` | core | — |
| `Testing` | core | — |
| `Analyzers` | — | `Microsoft.CodeAnalysis.CSharp` (build-time, `PrivateAssets="all"`) |

`Specifications`, `Testing`, `PostgreSql` and `SqlServer` are siblings: none references another
(`LayerDependencyTests`).

## Reference Rules

- **Intra-module** references are unconditional `ProjectReference`, in every build mode
  (ADR-GLOBAL-002 D5). A `CIReleaseBuild` condition on one is a violation.
- **Cross-module** references from `src/` use the two-ItemGroup pattern conditioned on
  `CIReleaseBuild` (root `.claude/rules/cross-module-references.md`). There are two:
  Abstractions → `MicroKit.Domain`, and core → `MicroKit.Result` (#197 phase 3; Abstractions never
  references Result, #196 D17).
- Test projects and the samples project reference other modules with an unconditional
  `ProjectReference`.

## Ecosystem

Allowed by the root graph: Persistence may depend on Result and Domain; it depends on both
(Abstractions → Domain, core → Result).
Persistence never depends on Messaging, Tenancy, MediatR or Http — they depend on it
(`MicroKit.MediatR.Behaviors` → Abstractions; `MicroKit.Tenancy.EntityFrameworkCore` →
Abstractions + EntityFrameworkCore). Core and EntityFrameworkCore do not reference
MediatR.Contracts (`LayerDependencyTests`).

## Package Confinement

| Package | Confined to |
|---------|-------------|
| `Microsoft.EntityFrameworkCore*` | EntityFrameworkCore and the providers |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSql |
| `Microsoft.EntityFrameworkCore.SqlServer` | SqlServer |
| `NSubstitute` | test projects |
| `Testcontainers.PostgreSql`, `Testcontainers.MsSql` | IntegrationTests |
| `Microsoft.CodeAnalysis.CSharp` | Analyzers (and test projects) |
| `FluentAssertions` | forbidden everywhere (root `.claude/rules/testing-libraries.md`) |

## Adding a Dependency

1. Check it is not already available transitively.
2. Add its version to the root `Directory.Packages.props`.
3. Put it in the lowest layer that needs it, never in Abstractions or core if it is EF Core.
4. A new cross-module edge updates the root CLAUDE.md graph and
   `.claude-context/context/dependency-graph.md`; any Abstractions change needs `api-reviewer`.
5. Run `dependency-guardian`.
