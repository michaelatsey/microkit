# Rule: Analyzers — MicroKit.Persistence

Applies to any task touching `MicroKit.Persistence.Analyzers` or its tests.

## Diagnostic Registry

| ID | Severity | Category | Title | Analyzer class |
|----|----------|----------|-------|----------------|
| `MKP001` | Error | Usage | Read repository calls CommitAsync or SaveChangesAsync | `ReadRepositoryMutationAnalyzer` |
| `MKP002` | Error | Usage | Read repository declares or calls a write method | `ReadRepositoryMutationAnalyzer` |
| `MKP003` | Warning | Usage | SaveChangesAsync called directly — bypasses IUnitOfWork | `DirectSaveChangesAnalyzer` |
| `MKP004` | Warning | Design | DbContext injected outside infrastructure layer | `DbContextInjectionAnalyzer` |
| `MKP005` | Error | Design | Repository method exposes IQueryable\<T\> as return type | `RepositoryIQueryableLeakAnalyzer` |

Categories are `MicroKit.Persistence.Usage` and `MicroKit.Persistence.Design` (`DiagnosticCategories`).

## Detection Strategies

### MKP001 + MKP002 — `ReadRepositoryMutationAnalyzer`

- **Invocation axis** (`RegisterOperationAction`, `OperationKind.Invocation`): the containing type
  implements `MicroKit.Persistence.IReadRepository<T>` and calls `CommitAsync`, `SaveChangesAsync` or `SaveChanges`
  (MKP001) or `AddAsync`/`UpdateAsync`/`DeleteAsync` (MKP002).
- **Declaration axis** (`RegisterSymbolAction`, `SymbolKind.Method`): a read repository declares one
  of the write methods (MKP002, at the method name).

`UpdateAsync` is not on any MicroKit contract; the analyzer still flags it as a write verb.

### MKP003 — `DirectSaveChangesAnalyzer`

- `SaveChanges`/`SaveChangesAsync` invoked on a receiver deriving from `DbContext`.
- Excluded: the containing type implements `IUnitOfWork` or `ITransactionalUnitOfWork`
  (`EfUnitOfWork<TContext>`).
- A `DbContext` subclass overriding `SaveChangesAsync` (auditing, events) is flagged; suppress
  with `#pragma warning disable MKP003`.

### MKP004 — `DbContextInjectionAnalyzer`

- `RegisterSymbolAction(SymbolKind.NamedType)`: a constructor parameter derives from `DbContext`
  and the class's namespace contains none of `Infrastructure`, `Persistence`,
  `EntityFrameworkCore`, `Repository`, `Data`.
- Heuristic: suppress for infrastructure in an unconventional namespace.

### MKP005 — `RepositoryIQueryableLeakAnalyzer`

- `RegisterSymbolAction(SymbolKind.Method)`: a public or internal method of a type implementing
  `IRepository<TAggregate, TId>` or `IReadRepository<T>` returns `IQueryable<T>`, possibly wrapped
  in `Task<>`/`ValueTask<>`. Reported at the method name.
- `IAsyncEnumerable<T>` is out of scope.

## Null-Guard Convention

`PersistenceSymbolHelper` is the single entry point for symbol resolution, by metadata name
(`MicroKit.Persistence.IReadRepository`1`, `MicroKit.Persistence.Abstractions.IRepository`2`,
`…Abstractions.IUnitOfWork`, `…EntityFrameworkCore.ITransactionalUnitOfWork`,
`Microsoft.EntityFrameworkCore.DbContext`). When `GetTypeByMetadataName` returns `null` the
package is not referenced and the helper returns `false`. Never assume a symbol is non-null.

## Adding a Diagnostic

1. Next ID (`MKP006`, …); category `Usage` (behaviour) or `Design` (structure).
2. Severity: `Error` for a definite violation, `Warning` for a heuristic.
3. Add the `DiagnosticDescriptor`, update `SupportedDiagnostics`, update this table.
4. Tests: at least one positive and one negative case.

## Packaging

`netstandard2.0`, `IncludeBuildOutput=false`; the DLL is packed to `analyzers/dotnet/cs/` — no
`lib/` folder. Consumers reference it with `PrivateAssets=all`. Inside the repository a project
references it with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`
(`samples/MicroKit.Persistence.Samples`, whose Release build fails on any MKP diagnostic).

## Testing

- `CSharpAnalyzerTest<TAnalyzer, CompatXUnitVerifier>` with inline markers `{|MKPxxx:code|}`.
- `PersistenceStubs.All` provides the MicroKit and `DbContext` stubs; their namespaces and names
  must match the metadata names the helper resolves.
- Auxiliary assertions may use Shouldly or a bare `Xunit.Assert` (root
  `.claude/rules/testing-libraries.md`); never FluentAssertions.
