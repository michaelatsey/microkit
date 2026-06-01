# Rule: Analyzers — MicroKit.Persistence

## Always active for any task touching `MicroKit.Persistence.Analyzers`.

## Diagnostic Registry

All 5 diagnostics share the `MKP` prefix (MicroKit Persistence).

| ID | Severity | Category | Title | Analyzer class |
|----|----------|----------|-------|----------------|
| `MKP001` | Error | Usage | Read repository calls CommitAsync or SaveChangesAsync | `ReadRepositoryMutationAnalyzer` |
| `MKP002` | Error | Usage | Read repository declares or calls a write method | `ReadRepositoryMutationAnalyzer` |
| `MKP003` | Warning | Usage | SaveChangesAsync called directly — bypasses IUnitOfWork | `DirectSaveChangesAnalyzer` |
| `MKP004` | Warning | Design | DbContext injected outside infrastructure layer | `DbContextInjectionAnalyzer` |
| `MKP005` | Error | Design | Repository method exposes IQueryable\<T\> as return type | `RepositoryIQueryableLeakAnalyzer` |

## Detection Strategies

### MKP001 + MKP002 — `ReadRepositoryMutationAnalyzer`

Two detection axes run in parallel:

**Axis 1 — Invocation (`RegisterOperationAction`):**
- Triggered on every `IInvocationOperation`.
- Checks if the method name is in the forbidden set and if the *containing type* implements
  `IReadRepository<T>` (either the Abstractions marker or the Core extension).
- CommitAsync/SaveChangesAsync → MKP001; AddAsync/UpdateAsync/DeleteAsync → MKP002.

**Axis 2 — Declaration (`RegisterSymbolAction(SymbolKind.Method)`):**
- Triggered for every method symbol.
- Catches classes implementing `IReadRepository<T>` that *declare* a mutation method
  (e.g., a developer adds `AddAsync` to a read repo). Reports MKP002 at the method name.

### MKP003 — `DirectSaveChangesAnalyzer`

- `RegisterOperationAction(OperationKind.Invocation)`.
- Checks `SaveChangesAsync` or `SaveChanges` called on a receiver deriving from `DbContext`.
- **Excluded:** containing type implements `IUnitOfWork` or `ITransactionalUnitOfWork`.
  This covers `EfUnitOfWork<TContext>`, the only legitimate caller.
- **Known suppression case:** custom `DbContext` subclasses overriding `SaveChangesAsync`
  for auditing or event dispatch are not `IUnitOfWork` implementations and will be flagged.
  Suppress with `#pragma warning disable MKP003`.

### MKP004 — `DbContextInjectionAnalyzer`

- `RegisterSymbolAction(SymbolKind.NamedType)`.
- Checks every class constructor. If a parameter type derives from `DbContext` AND the
  class's namespace does NOT contain any of:
  `Infrastructure`, `Persistence`, `EntityFrameworkCore`, `Repository`, `Data`
  → reports MKP004 at the parameter type syntax node.
- **Best-effort heuristic.** Namespace conventions vary. Suppress with
  `#pragma warning disable MKP004` for legitimate infrastructure classes that use
  unconventional namespaces (e.g., `MyApp.Storage`, `MyApp.Db`).

### MKP005 — `RepositoryIQueryableLeakAnalyzer`

- `RegisterSymbolAction(SymbolKind.Method)`.
- For each public/internal method on a type implementing `IRepository<T>` or `IReadRepository<T>`,
  unwraps `Task<>` / `ValueTask<>` from the return type and checks for `IQueryable<T>`.
- Reports MKP005 at the method name.
- `IAsyncEnumerable<T>` is **not** covered — its internal query source is not visible at
  the signature level. This is explicitly out of scope for v1.
- **Detection site:** the method *definition*, not the call site (catches the root cause).

## Null-Guard Convention (mandatory for all future diagnostics)

`PersistenceSymbolHelper` is the single entry point for symbol resolution. Every helper
method null-guards: if `GetTypeByMetadataName` returns `null` (the target package is not
referenced by the compilation), the method returns `false` immediately. All analyzer
`Initialize` registrations rely on this — **never** assume a type symbol is non-null.

## Adding a New Diagnostic

1. Assign the next available ID (`MKP006`, `MKP007`, …).
2. Choose the category: `Usage` for behavioral violations, `Design` for structural violations.
3. Assign severity: `Error` for definite violations, `Warning` for heuristic/context-dependent ones.
4. Add a `DiagnosticDescriptor` to the relevant analyzer class (or create a new one).
5. Update `SupportedDiagnostics` if adding to an existing class.
6. Update this registry table.
7. Write tests: at least one positive case (triggers) and one negative case (no trigger).

## Project Structure

```
src/MicroKit.Persistence.Analyzers/
├── MicroKit.Persistence.Analyzers.csproj   ← netstandard2.0; pack to analyzers/dotnet/cs/
├── GlobalUsings.cs
├── DiagnosticCategories.cs                 ← Usage + Design
├── Helpers/
│   └── PersistenceSymbolHelper.cs          ← all symbol resolution + null-guards
├── ReadRepositoryMutationAnalyzer.cs       ← MKP001 + MKP002
├── DirectSaveChangesAnalyzer.cs            ← MKP003
├── DbContextInjectionAnalyzer.cs           ← MKP004
└── RepositoryIQueryableLeakAnalyzer.cs     ← MKP005
```

## Consuming Projects

```xml
<PackageReference Include="MicroKit.Persistence.Analyzers">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

The package has no `lib/` folder — it is build-time only.

## Testing Rules

- **Shouldly is mandatory** for all auxiliary assertions in analyzer tests.
- Test framework: `CSharpAnalyzerTest<TAnalyzer, CompatXUnitVerifier>`.
- Use inline diagnostic markers: `{|MKPxxx:code|}` to mark expected diagnostic locations.
- Stubs: all MicroKit.Persistence types and DbContext are provided inline via `PersistenceStubs.All`.
  The stub namespace/type names must match the exact metadata names the analyzers resolve.
- Every diagnostic must have at minimum:
  - One positive test (triggers the diagnostic).
  - One negative test (valid code — no diagnostic).
