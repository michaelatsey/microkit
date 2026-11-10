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
| `Testing` | core | — (the Docker probe is BCL only) |
| `Testing.PostgreSql` | Testing | `Testcontainers.PostgreSql` |
| `Testing.SqlServer` | Testing | `Testcontainers.MsSql` |
| `Analyzers` | — | `Microsoft.CodeAnalysis.CSharp` (build-time, `PrivateAssets="all"`) |

Two projects grant `InternalsVisibleTo`, and no other:
- core → `EntityFrameworkCore`, `Testing`, `UnitTests`, for the keyset cursor's codec and rules (ADR-013);
- `Testing` → `Testing.PostgreSql`, `Testing.SqlServer` (the shared container lifecycle and connection-string edit,
  ADR-015) and `UnitTests`, a test assembly that is never packed (the lifecycle and the Docker probe's seams).

The grantees ship with their grantor under one version, or are never packed.

`Specifications`, `Testing`, `PostgreSql` and `SqlServer` are siblings: none references another
(`LayerDependencyTests`). `Testing.PostgreSql` and `Testing.SqlServer` sit above `Testing`, not beside it: they
reference no EF Core, no provider package, not `Specifications` and not each other, and bring no test framework
(`LayerDependencyTests`, and `PackageClosureTests` over the resolved graph).

## Reference Rules

- **Intra-module** references are unconditional `ProjectReference`, in every build mode
  (ADR-GLOBAL-002 D5). A build-mode condition on one is a violation.
- **Cross-module** references are unconditional `ProjectReference` too, in every build mode and in
  every project — `src/`, tests and samples (#197 Spec, Perimeter). No project of the module references
  a MicroKit package published on NuGet.org; for this module this overrides the two-ItemGroup pattern of
  the root `.claude/rules/cross-module-references.md`. The `src/` edges are Abstractions →
  `MicroKit.Domain` and core → `MicroKit.Result` (Abstractions never references Result, #196 D17).

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
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSql (and IntegrationTests, which uses it directly) |
| `Microsoft.EntityFrameworkCore.Sqlite` | IntegrationTests |
| `Microsoft.EntityFrameworkCore.SqlServer` | SqlServer |
| `NSubstitute` | test projects |
| `Testcontainers.PostgreSql`, `Testcontainers.MsSql` | `Testing.PostgreSql`, `Testing.SqlServer` respectively; IntegrationTests, ArchitectureTests and the samples receive them through those |
| xunit, NUnit, MSTest | test projects only — never a `src/` package (`PackageClosureTests`) |
| `Microsoft.CodeAnalysis.CSharp` | Analyzers (and test projects) |
| `FluentAssertions` | forbidden everywhere (root `.claude/rules/testing-libraries.md`) |

## Adding a Dependency

1. Check it is not already available transitively.
2. Add its version to the root `Directory.Packages.props`.
3. Put it in the lowest layer that needs it, never in Abstractions or core if it is EF Core.
4. A new cross-module edge updates the root CLAUDE.md graph and
   `.claude-context/context/dependency-graph.md`; any Abstractions change needs `api-reviewer`.
5. Run `dependency-guardian`.
